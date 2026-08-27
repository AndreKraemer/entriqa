using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Entriqa.Application.Pipeline;
using Entriqa.Application.Pipeline.Steps;
using Entriqa.Application.Ports;
using Entriqa.Application.Security;
using Entriqa.Domain.Forms;
using Xunit;

namespace Entriqa.Tests;

public class PublishCheckServiceTests
{
    private static PublishCheckService Build()
    {
        var mail = Substitute.For<ISendTransactionalMailPort>();
        var steps = new ISubmissionStep[]
        {
            new NotifyMailStep(mail),
            new DoiRequestStep(mail, TestData.Tokens()),
            new BrevoContactStep(Substitute.For<IUpsertBrevoContactPort>(), TestData.Time),
            new LeadMagnetLinkStep(Substitute.For<ICreateDownloadLinkPort>()),
            new ReportingCloudPdfStep(Substitute.For<IMergeDocumentPort>(), Substitute.For<IStoreArtifactPort>(), TestData.Time),
            new BrevoMailStep(mail, Substitute.For<IStoreArtifactPort>(), Substitute.For<ICreateDownloadLinkPort>()),
            new WebhookCallStep(Substitute.For<IPostWebhookPort>()),
        };
        var pipeline = new SubmissionPipelineService(steps, Options.Create(TestData.Options()), TestData.Time, NullLogger<SubmissionPipelineService>.Instance);
        return new PublishCheckService(pipeline);
    }

    [Fact]
    public void GivenValidContactForm_WhenCheckingBeforePublish_ThenNoIssuesAreReported() => Assert.Empty(Build().Check(TestData.Contact()));

    [Fact]
    public void GivenLeadMagnetWithoutFileAndMailAttachingAMissingArtifact_WhenCheckingBeforePublish_ThenBothStepsAreFlagged()
    {
        var form = TestData.Contact() with
        {
            Pipeline = new[]
            {
                new StepDefinition("s1", "doi.request", "always", TestData.Json("""{"templateId":3}""")),
                new StepDefinition("s2", "brevo.mail", "always", TestData.Json("""{"templateId":9,"attach":"report"}""")),
                new StepDefinition("s3", "leadmagnet.link", "always", TestData.Json("""{}""")),
            },
        };

        var issues = Build().Check(form);

        Assert.Contains(issues, i => i.Contains("Schritt 2") && i.Contains("PDF"));
        Assert.Contains(issues, i => i.Contains("Schritt 3") && i.Contains("keine Datei"));
    }

    [Fact]
    public void GivenBrevoListWithoutDoiAndQuizPdfMissingTemplates_WhenCheckingBeforePublish_ThenEachGapIsReported()
    {
        var form = TestData.Contact() with
        {
            Quiz = TestData.Quiz(),
            Pipeline = new[]
            {
                new StepDefinition("s1", "brevo.contact", "hasEmail", TestData.Json("""{"listIds":[8]}""")),
                new StepDefinition("s2", "reportingcloud.pdf", "hasEmail", TestData.Json("""{"templates":{"legacy":"a.docx"}}""")),
            },
        };

        var issues = Build().Check(form);

        Assert.Contains(issues, i => i.Contains("ohne vorheriges Double-Opt-In"));
        Assert.Contains(issues, i => i.Contains("Mitte"));
        Assert.Contains(issues, i => i.Contains("Modern"));
    }

    [Fact]
    public void GivenDoiStepWithoutEmailAndConsentFields_WhenCheckingBeforePublish_ThenBothAreReportedAsMissing()
    {
        var form = TestData.Contact() with
        {
            Fields = new[] { new FieldDefinition("name", FieldTypes.Text, "Name") },
            Pipeline = new[] { new StepDefinition("s1", "doi.request", "always", TestData.Json("""{"templateId":3}""")) },
        };

        var issues = Build().Check(form);

        Assert.Contains(issues, i => i.Contains("kein E-Mail-Feld"));
        Assert.Contains(issues, i => i.Contains("keine Einwilligung"));
    }
}
