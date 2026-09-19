using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Entriqa.Application;
using Entriqa.Application.Pipeline;
using Entriqa.Application.Pipeline.Steps;
using Entriqa.Application.Ports;
using Entriqa.Application.Security;
using Entriqa.Application.UseCases;
using Entriqa.Domain.Errors;
using Entriqa.Domain.Forms;
using Entriqa.Domain.Submissions;
using Entriqa.Domain.UseCases;
using Entriqa.Domain.Validation;
using Xunit;

namespace Entriqa.Tests;

/// <summary>
/// Test mode (#21): a draft runs through the whole pipeline without side effects. Mail steps go to the
/// signed-in admin, steps with an external write are suppressed and describe what they would have done,
/// every step gets a verdict, and nothing is stored. These are the failing proof for the story.
/// </summary>
public class FormTestModeTests
{
    private const string Admin = "admin@example.org";

    private sealed record Rig(SubmissionPipelineService Pipeline, ISendTransactionalMailPort Mail,
        IUpsertBrevoContactPort Contacts, IPostWebhookPort Webhook);

    private static Rig BuildPipeline()
    {
        var mail = Substitute.For<ISendTransactionalMailPort>();
        var contacts = Substitute.For<IUpsertBrevoContactPort>();
        var webhook = Substitute.For<IPostWebhookPort>();             // both webhook.call and teams.notify post through this port
        var steps = new ISubmissionStep[]
        {
            new NotifyMailStep(mail),
            new DoiRequestStep(mail, TestData.Tokens()),
            new BrevoContactStep(contacts, TestData.Time),
            new BrevoMailStep(mail, Substitute.For<IStoreArtifactPort>(), Substitute.For<ICreateDownloadLinkPort>()),
            new WebhookCallStep(webhook),
            new TeamsNotifyStep(webhook),
        };
        var pipeline = new SubmissionPipelineService(steps, Options.Create(TestData.Options()),
            TestData.Time, NullLogger<SubmissionPipelineService>.Instance);
        return new Rig(pipeline, mail, contacts, webhook);
    }

    private static FormDefinition NoDoiForm() => TestData.Contact() with
    {
        Pipeline = new[]
        {
            new StepDefinition("s1", "notify.mail", "always", TestData.Json("""{"to":"ops@example.com","templateId":1}""")),
            new StepDefinition("s2", "webhook.call", "always", TestData.Json("""{"url":"https://hook.example.com/x"}""")),
            new StepDefinition("s3", "teams.notify", "always", TestData.Json("""{"webhookUrl":"https://teams.example.com/x"}""")),
        },
    };

    private static FormDefinition DoiForm() => TestData.Contact() with
    {
        Pipeline = new[]
        {
            new StepDefinition("s1", "notify.mail", "always", TestData.Json("""{"to":"ops@example.com","templateId":1}""")),
            new StepDefinition("s2", "doi.request", "always", TestData.Json("""{"templateId":2}""")),
            new StepDefinition("s3", "brevo.contact", "always", TestData.Json("""{"listIds":[7,8]}""")),
            new StepDefinition("s4", "brevo.mail", "always", TestData.Json("""{"templateId":5}""")),
        },
    };

    private static Submission SubmissionFor(FormDefinition form, SubmissionPipelineService pipeline) => new()
    {
        Id = form.Slug + ":test", Slug = form.Slug, Version = 0, CreatedAt = TestData.Time.GetUtcNow(),
        Locale = "de", Email = "visitor@example.com", FirstName = "Max",
        Values = new() { ["name"] = "Max Mustermann", ["email"] = "visitor@example.com", ["msg"] = "Hallo", ["consent"] = "on" },
        StepRuns = pipeline.CreateRuns(form),
    };

    private static Task SentTo(ISendTransactionalMailPort mail, string to) =>
        mail.SendAsync(to, Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<IReadOnlyDictionary<string, object?>>(),
            Arg.Any<MailAttachment?>(), Arg.Any<CancellationToken>());

    // AC4 + AC5: mail is redirected to the admin, and the writing steps never touch their real services.
    [Fact]
    public async Task GivenAFormWithoutDoubleOptIn_WhenRunAsATest_ThenMailGoesToTheAdminAndTheWritingStepsAreNotCalled()
    {
        var rig = BuildPipeline();
        var form = NoDoiForm();

        await rig.Pipeline.RunTestAsync(SubmissionFor(form, rig.Pipeline), form, 0, Admin);

        await SentTo(rig.Mail.Received(), Admin);
        await rig.Webhook.DidNotReceiveWithAnyArgs().PostJsonAsync(default!, default!, default);
    }

    // AC6 + AC8: a suppressed step's verdict is "skipped", with the resolved values it would have used.
    [Fact]
    public async Task GivenASuppressedWritingStep_WhenRunAsATest_ThenItsVerdictIsSkippedWithTheResolvedValues()
    {
        var rig = BuildPipeline();
        var form = NoDoiForm();

        var outcomes = await rig.Pipeline.RunTestAsync(SubmissionFor(form, rig.Pipeline), form, 0, Admin);

        var webhook = outcomes.Single(o => o.StepId == "s2");
        Assert.Equal(StepRunStatus.Skipped, webhook.Status);
        Assert.Contains(webhook.Notes, n => n.Value.Contains("hook.example.com", StringComparison.Ordinal));
    }

    // AC3 + AC8 (and the "walk the whole pipeline" decision): every step past the confirmation gate still
    // gets a verdict - the suppressed one skipped, the mail one actually sent to the admin - and none is left waiting.
    [Fact]
    public async Task GivenADoubleOptInForm_WhenRunAsATest_ThenStepsAfterTheConfirmationAlsoGetAVerdict()
    {
        var rig = BuildPipeline();
        var form = DoiForm();

        var outcomes = await rig.Pipeline.RunTestAsync(SubmissionFor(form, rig.Pipeline), form, 0, Admin);

        Assert.Equal(StepRunStatus.Skipped, outcomes.Single(o => o.StepId == "s3").Status);   // brevo.contact, after DOI
        Assert.Equal(StepRunStatus.Ok, outcomes.Single(o => o.StepId == "s4").Status);         // brevo.mail, after DOI
        Assert.DoesNotContain(outcomes, o => o.Status == StepRunStatus.Waiting);
    }

    // AC8: a mail step whose send fails is reported as failed, with the error text.
    [Fact]
    public async Task GivenAMailStepWhoseSendFails_WhenRunAsATest_ThenItsVerdictIsFailedWithTheError()
    {
        var rig = BuildPipeline();
        var form = NoDoiForm();
        rig.Mail.WhenForAnyArgs(m => m.SendAsync(default!, default, default, default!, default, default))
            .Do(_ => throw new InvalidOperationException("smtp down"));

        var outcomes = await rig.Pipeline.RunTestAsync(SubmissionFor(form, rig.Pipeline), form, 0, Admin);

        var notify = outcomes.Single(o => o.StepId == "s1");
        Assert.Equal(StepRunStatus.Failed, notify.Status);
        Assert.Contains("smtp down", notify.Error ?? "", StringComparison.Ordinal);
    }

    // AC4, edge from the issue: no usable admin address -> report it, never send into the void.
    [Fact]
    public async Task GivenNoAdminAddress_WhenRunAsATest_ThenMailStepsAreReportedAsFailedAndNoMailIsSent()
    {
        var rig = BuildPipeline();
        var form = NoDoiForm();

        var outcomes = await rig.Pipeline.RunTestAsync(SubmissionFor(form, rig.Pipeline), form, 0, adminMailTo: null);

        Assert.Equal(StepRunStatus.Failed, outcomes.Single(o => o.StepId == "s1").Status);
        await rig.Mail.DidNotReceiveWithAnyArgs().SendAsync(default!, default, default, default!, default, default);
    }

    // AC9: the confirm link inside a test mail confirms nothing - it is recognised as a test, no lookup, no error.
    [Fact]
    public async Task GivenAConfirmTokenFromATestMail_WhenConfirming_ThenTheResultIsMarkedAsTestAndNothingIsLookedUp()
    {
        var opts = Options.Create(TestData.Options());
        var tokens = new FormTokenService(opts, TestData.Time);
        var getSubmission = Substitute.For<ITryGetSubmissionQuery>();
        var pipeline = new SubmissionPipelineService(
            [new NotifyMailStep(Substitute.For<ISendTransactionalMailPort>())], opts, TestData.Time,
            NullLogger<SubmissionPipelineService>.Instance);
        var useCase = new ConfirmSubmissionUseCase(
            getSubmission, Substitute.For<ITryGetFormVersionQuery>(), Substitute.For<ISaveSubmissionCommand>(),
            tokens, new IpHasher(opts), pipeline, TestData.ConsentProofs().Service, opts, TestData.Time);
        var token = tokens.Issue(FormTokenService.KindConfirmTest, "kontakt:test");

        var result = await useCase.ExecuteAsync(token, null);

        Assert.True(result.IsTest);
        await getSubmission.DidNotReceiveWithAnyArgs().ExecuteAsync(default!, default);
    }

    // AC2: a test always runs against the draft, even when a different published version exists.
    [Fact]
    public async Task GivenADraftThatDiffersFromThePublishedVersion_WhenRunningATest_ThenTheDraftPipelineIsWalked()
    {
        var rig = BuildPipeline();
        var draftDef = TestData.Contact() with
        {
            Pipeline = new[] { new StepDefinition("draft-only", "notify.mail", "always", TestData.Json("""{"to":"ops@example.com","templateId":9}""")) },
        };
        var getDraft = Substitute.For<ITryGetFormDraftQuery>();
        getDraft.ExecuteAsync("kontakt", Arg.Any<CancellationToken>())
            .Returns(new FormDraft("kontakt", "draft", 7, TestData.Time.GetUtcNow(), "seed", draftDef));
        var useCase = new RunFormTestUseCase(getDraft, rig.Pipeline, Options.Create(TestData.Options()), TestData.Time);

        var result = await useCase.ExecuteAsync(new FormTestRequest("kontakt", "de",
            new() { ["name"] = "Max", ["email"] = "visitor@example.com", ["msg"] = "Hi", ["consent"] = "on" }, null, Admin));

        Assert.Contains(result.Steps, o => o.StepId == "draft-only");
    }

    // AC3: the same validation as a real submission runs - invalid input fails the test, it does not pass silently.
    [Fact]
    public async Task GivenInvalidInput_WhenRunningATest_ThenValidationFailsLikeARealSubmission()
    {
        var rig = BuildPipeline();
        var getDraft = Substitute.For<ITryGetFormDraftQuery>();
        getDraft.ExecuteAsync("kontakt", Arg.Any<CancellationToken>())
            .Returns(new FormDraft("kontakt", "draft", 0, TestData.Time.GetUtcNow(), "seed", TestData.Contact()));
        var useCase = new RunFormTestUseCase(getDraft, rig.Pipeline, Options.Create(TestData.Options()), TestData.Time);

        // "msg" is required and missing -> the validator must reject it.
        await Assert.ThrowsAsync<ValidationException>(() => useCase.ExecuteAsync(
            new FormTestRequest("kontakt", "de", new() { ["name"] = "Max", ["email"] = "visitor@example.com", ["consent"] = "on" }, null, Admin)));
    }

    // AC11: the test endpoint is never reachable without the admin role. Source guard - Functions is not
    // referenced by the test host, so the role check is asserted by reading the source (mutation-verify after impl).
    [Fact]
    public void TheTestFormEndpointRequiresTheAdminRole()
    {
        var source = ReadFunctionSource("AdminFunctions.cs");
        var blocks = Regex.Split(source, @"(?=\[Function\()");
        var testBlocks = blocks.Where(b => b.Contains("forms/{slug}/test", StringComparison.Ordinal)).ToList();
        Assert.NotEmpty(testBlocks);    // red until the endpoint exists; after impl, dropping RequireRole must fail the All below
        Assert.All(testBlocks, b => Assert.Contains("RequireRole(req, \"admin\")", b, StringComparison.Ordinal));
    }

    private static string ReadFunctionSource(string fileName)
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !Directory.Exists(Path.Combine(dir, "src", "Hosts", "Entriqa.Functions")))
            dir = Directory.GetParent(dir)?.FullName;
        Assert.NotNull(dir);
        var path = Directory.GetFiles(Path.Combine(dir!, "src", "Hosts", "Entriqa.Functions"), fileName, SearchOption.AllDirectories).Single();
        return File.ReadAllText(path);
    }
}
