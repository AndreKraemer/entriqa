using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Entriqa.Application.Pipeline;
using Entriqa.Application.Ports;
using Entriqa.Application.UseCases;
using Entriqa.Domain.Forms;
using Entriqa.Domain.Submissions;
using Xunit;

namespace Entriqa.Tests;

public class HousekeepingUseCaseTests
{
    private sealed class FakePdfStep : ISubmissionStep
    {
        public int Calls;
        public string Key => "reportingcloud.pdf"; public string Name => Key; public string Description => "";
        public StepMode Mode => StepMode.Deferred; public string ConfigSchema => "{}";
        public Task<StepResult> ExecuteAsync(StepContext ctx, JsonElement config, CancellationToken ct)
        { Calls++; return Task.FromResult(StepResult.Ok); }
    }

    private static (RunHousekeepingUseCase uc, FakePdfStep pdf,
        IListHousekeepingSubmissionsQuery list, ISaveSubmissionCommand save,
        IDeleteSubmissionCommand delete, IStoreArtifactPort artifacts) Build()
    {
        var pdf = new FakePdfStep();
        var pipeline = new SubmissionPipelineService(new ISubmissionStep[] { pdf }, Options.Create(TestData.Options()), TestData.Time, NullLogger<SubmissionPipelineService>.Instance);
        var list = Substitute.For<IListHousekeepingSubmissionsQuery>();
        var getVersion = Substitute.For<ITryGetFormVersionQuery>();
        var save = Substitute.For<ISaveSubmissionCommand>();
        var delete = Substitute.For<IDeleteSubmissionCommand>();
        var purge = Substitute.For<IPurgeSecurityEntriesCommand>();
        var recordRun = Substitute.For<IRecordHousekeepingRunCommand>();
        var artifacts = Substitute.For<IStoreArtifactPort>();
        purge.ExecuteAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns((2, 3));

        var form = TestData.Contact() with { Pipeline = new[] { new StepDefinition("s1", "reportingcloud.pdf", "always", TestData.Json("{}")) } };
        getVersion.ExecuteAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new FormVersion("kontakt", 1, form, TestData.Time.GetUtcNow(), "test"));

        var listArtifacts = Substitute.For<IListArtifactsPort>();
        listArtifacts.ListAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Array.Empty<ArtifactInfo>());
        var uc = new RunHousekeepingUseCase(list, getVersion, save, delete, purge, recordRun, artifacts, listArtifacts, pipeline,
            Options.Create(TestData.Options()), TestData.Time, NullLogger<RunHousekeepingUseCase>.Instance);
        return (uc, pdf, list, save, delete, artifacts);
    }

    private static Submission Unfinished(string id, StepRunStatus status, int attempts = 0) => new()
    {
        Id = id, Slug = "kontakt", Version = 1, CreatedAt = TestData.Time.GetUtcNow().AddHours(-1), Values = new(),
        StepRuns = new List<StepRun> { new() { StepId = "s1", StepKey = "reportingcloud.pdf", Phase = StepPhase.OnSubmit, Status = status, Attempts = attempts } },
    };

    [Fact]
    public async Task GivenPendingDeferredStep_WhenHousekeepingRuns_ThenTheStepIsSweptAndTheSubmissionSaved()
    {
        var (uc, pdf, list, save, _, _) = Build();
        var s = Unfinished("kontakt:1", StepRunStatus.Pending);
        list.ListUnfinishedAsync(Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new[] { s });
        list.ListExpiredAsync(default, default, default).ReturnsForAnyArgs(Array.Empty<Submission>());

        var result = await uc.ExecuteAsync();

        Assert.Equal(1, result.Swept);
        Assert.Equal(0, result.Retried);
        Assert.Equal(1, pdf.Calls);
        Assert.Equal(StepRunStatus.Ok, s.Run("s1").Status);
        await save.Received(1).ExecuteAsync(s, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GivenFailedStepsBelowAndAtTheAttemptLimit_WhenHousekeepingRuns_ThenOnlyTheOneBelowIsRetried()
    {
        var (uc, pdf, list, save, _, _) = Build();
        var fresh = Unfinished("kontakt:1", StepRunStatus.Failed, attempts: 1);
        var exhausted = Unfinished("kontakt:2", StepRunStatus.Failed, attempts: 3);   // AutoRetryMax = 3
        list.ListUnfinishedAsync(Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new[] { fresh, exhausted });
        list.ListExpiredAsync(default, default, default).ReturnsForAnyArgs(Array.Empty<Submission>());

        var result = await uc.ExecuteAsync();

        Assert.Equal(1, result.Retried);
        Assert.Equal(StepRunStatus.Ok, fresh.Run("s1").Status);
        Assert.Equal(StepRunStatus.Failed, exhausted.Run("s1").Status);               // left behind for the admin to handle
        await save.DidNotReceive().ExecuteAsync(exhausted, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GivenExpiredSubmissionWithBlobAndUrlArtifacts_WhenRetentionRuns_ThenOnlyBlobArtifactsAreDeletedWithIt()
    {
        var (uc, _, list, _, delete, artifacts) = Build();
        var s = Unfinished("kontakt:old", StepRunStatus.Ok);
        s.Artifacts["report"] = "reports/kontakt-old.pdf";
        s.Artifacts["download"] = "https://blob.example/leadmagnets/x.pdf?sig=…";
        list.ListUnfinishedAsync(default, default).ReturnsForAnyArgs(Array.Empty<Submission>());
        list.ListExpiredAsync(default, default, default).ReturnsForAnyArgs(new[] { s });

        var result = await uc.ExecuteAsync();

        Assert.Equal(1, result.Deleted);
        Assert.Equal(2, result.NoncesPurged);
        Assert.Equal(3, result.RateLimitsPurged);
        await artifacts.Received(1).DeleteAsync("reports/kontakt-old.pdf", Arg.Any<CancellationToken>());
        await artifacts.DidNotReceive().DeleteAsync(Arg.Is<string>(p => p.StartsWith("https")), Arg.Any<CancellationToken>());
        await delete.Received(1).ExecuteAsync(s, Arg.Any<CancellationToken>());
    }
}
