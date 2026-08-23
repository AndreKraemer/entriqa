using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Entriqa.Application.Pipeline;
using Entriqa.Domain.Forms;
using Entriqa.Domain.Submissions;
using Xunit;

namespace Entriqa.Tests;

public class SubmissionPipelineServiceTests
{
    private sealed class FakeStep(string key, StepMode mode = StepMode.Inline, bool splits = false, Func<StepContext, StepResult>? run = null) : ISubmissionStep
    {
        public List<string> Calls { get; } = new();
        public string Key => key; public string Name => key; public string Description => ""; public StepMode Mode => mode;
        public bool SplitsPhase => splits; public string ConfigSchema => "{}";
        public Task<StepResult> ExecuteAsync(StepContext ctx, JsonElement config, CancellationToken ct)
        { Calls.Add(ctx.Submission.Id); return Task.FromResult(run?.Invoke(ctx) ?? StepResult.Ok); }
    }

    private static (SubmissionPipelineService svc, FakeStep doi, FakeStep link, FakeStep mail, FakeStep pdf) Build(Func<StepContext, StepResult>? mailRun = null)
    {
        var doi = new FakeStep("doi.request", splits: true);
        var link = new FakeStep("leadmagnet.link");
        var mail = new FakeStep("brevo.mail", run: mailRun);
        var pdf = new FakeStep("reportingcloud.pdf", StepMode.Deferred);
        var svc = new SubmissionPipelineService(new ISubmissionStep[] { doi, link, mail, pdf }, Options.Create(TestData.Options()), TestData.Time, NullLogger<SubmissionPipelineService>.Instance);
        return (svc, doi, link, mail, pdf);
    }

    private static FormDefinition Form(params (string id, string key, string when)[] steps) => TestData.Contact() with
    {
        Pipeline = steps.Select(s => new StepDefinition(s.id, s.key, s.when, TestData.Json("{}"))).ToList(),
    };

    private static Submission Sub(SubmissionPipelineService svc, FormDefinition form, string? email = "a@b.de") => new()
    {
        Id = "kontakt:1", Slug = "kontakt", Version = 1, CreatedAt = TestData.Time.GetUtcNow(), Values = new(), Email = email, StepRuns = svc.CreateRuns(form),
    };

    [Fact]
    public async Task Steps_after_doi_wait_until_confirmed_then_run()
    {
        var (svc, doi, link, mail, _) = Build();
        var form = Form(("s1", "doi.request", "always"), ("s2", "leadmagnet.link", "always"), ("s3", "brevo.mail", "always"));
        var s = Sub(svc, form);

        var deferred = await svc.RunAsync(s, form, 1, RunMode.Inline);

        Assert.False(deferred);
        Assert.Equal(StepRunStatus.Ok, s.Run("s1").Status);
        Assert.Equal(StepRunStatus.Waiting, s.Run("s2").Status);
        Assert.Equal(StepRunStatus.Waiting, s.Run("s3").Status);
        Assert.Equal(SubmissionState.AwaitingConfirmation, s.State);
        Assert.Empty(link.Calls);

        s.ConfirmedAt = TestData.Time.GetUtcNow();
        await svc.RunAsync(s, form, 1, RunMode.Deferred);

        Assert.All(s.StepRuns, r => Assert.Equal(StepRunStatus.Ok, r.Status));
        Assert.Single(doi.Calls);                       // nicht erneut verschickt
        Assert.Single(mail.Calls);
    }

    [Fact]
    public async Task Deferred_step_splits_the_run_and_holds_back_everything_after_it()
    {
        var (svc, _, _, mail, pdf) = Build();
        var form = Form(("s1", "reportingcloud.pdf", "always"), ("s2", "brevo.mail", "always"));
        var s = Sub(svc, form);

        var deferred = await svc.RunAsync(s, form, 1, RunMode.Inline);

        Assert.True(deferred);
        Assert.Equal(StepRunStatus.Pending, s.Run("s1").Status);
        Assert.Equal(StepRunStatus.Pending, s.Run("s2").Status);  // läuft NICHT inline: sonst fehlte ihr das report-Artefakt des Deferred-Produzenten
        Assert.Empty(pdf.Calls);
        Assert.Empty(mail.Calls);

        await svc.RunAsync(s, form, 1, RunMode.Deferred);
        Assert.Equal(StepRunStatus.Ok, s.Run("s1").Status);
        Assert.Equal(StepRunStatus.Ok, s.Run("s2").Status);
        Assert.Single(pdf.Calls);
        Assert.Single(mail.Calls);
    }

    [Fact]
    public async Task Inline_steps_before_a_deferred_step_still_run_inline()
    {
        var (svc, _, link, mail, _) = Build();
        var form = Form(("s1", "leadmagnet.link", "always"), ("s2", "reportingcloud.pdf", "always"), ("s3", "brevo.mail", "always"));
        var s = Sub(svc, form);

        var deferred = await svc.RunAsync(s, form, 1, RunMode.Inline);

        Assert.True(deferred);
        Assert.Equal(StepRunStatus.Ok, s.Run("s1").Status);
        Assert.Equal(StepRunStatus.Pending, s.Run("s2").Status);
        Assert.Equal(StepRunStatus.Pending, s.Run("s3").Status);
        Assert.Single(link.Calls);
        Assert.Empty(mail.Calls);
    }

    [Fact]
    public async Task Failure_blocks_followers_and_retry_resumes()
    {
        var fail = true;
        var (svc, _, _, _, _) = Build(_ => fail ? StepResult.Failed("Brevo 429") : StepResult.Ok);
        var form = Form(("s1", "leadmagnet.link", "always"), ("s2", "brevo.mail", "always"), ("s3", "reportingcloud.pdf", "always"));
        var s = Sub(svc, form);

        await svc.RunAsync(s, form, 1, RunMode.Deferred);
        Assert.Equal(StepRunStatus.Failed, s.Run("s2").Status);
        Assert.Equal("Brevo 429", s.Run("s2").Error);
        Assert.Equal(StepRunStatus.Blocked, s.Run("s3").Status);
        Assert.Equal(SubmissionState.Failed, s.State);

        fail = false;
        await svc.RunAsync(s, form, 1, RunMode.Retry, onlyStepId: "s2");
        Assert.All(s.StepRuns, r => Assert.Equal(StepRunStatus.Ok, r.Status));
        Assert.Equal(2, s.Run("s2").Attempts);
    }

    [Fact]
    public async Task HasEmail_condition_skips_without_email()
    {
        var (svc, _, _, mail, _) = Build();
        var form = Form(("s1", "brevo.mail", "hasEmail"));
        var s = Sub(svc, form, email: null);

        await svc.RunAsync(s, form, 1, RunMode.Inline);

        Assert.Equal(StepRunStatus.Skipped, s.Run("s1").Status);
        Assert.Empty(mail.Calls);
        Assert.Equal(SubmissionState.Done, s.State);
    }

    [Fact]
    public async Task Exception_in_step_becomes_failed_run()
    {
        var (svc, _, _, _, _) = Build(_ => throw new HttpRequestException("boom"));
        var form = Form(("s1", "brevo.mail", "always"));
        var s = Sub(svc, form);

        await svc.RunAsync(s, form, 1, RunMode.Inline);

        Assert.Equal(StepRunStatus.Failed, s.Run("s1").Status);
        Assert.Equal("boom", s.Run("s1").Error);
    }
}
