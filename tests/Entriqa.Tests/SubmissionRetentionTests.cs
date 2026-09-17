using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Entriqa.Application.Pipeline;
using Entriqa.Application.Ports;
using Entriqa.Application.UseCases;
using Entriqa.Domain.Submissions;
using Entriqa.Domain.UseCases;
using Xunit;

namespace Entriqa.Tests;

/// <summary>
/// Issue #15: an admin can see when a submission auto-deletes and can retain it permanently, extend the
/// deadline by a fixed period, or lift that exception again - and housekeeping has to honour it. AC2 (the
/// list highlight) is acceptance-only: it has no runtime surface without a render host, and the markup
/// does not exist yet, so a source guard written now would only anchor on names the implementation has not
/// chosen. AC8 (the consent proof from #1 is untouched) is structural: nothing built here takes a
/// dependency on ConsentProofService, so nothing here can violate it.
/// </summary>
public class SubmissionRetentionTests
{
    private const string Me = TestData.AdminMe;
    private const string Id = "kontakt:0900";
    private static readonly int RetentionDays = TestData.Options().RetentionDays;

    private static Submission Stored(DateTimeOffset createdAt, DateTimeOffset? retainUntil = null) => new()
    {
        Id = Id,
        Slug = "kontakt",
        Version = 1,
        CreatedAt = createdAt,
        Values = new Dictionary<string, string> { ["email"] = "eva@example.org" },
        Email = "eva@example.org",
        Handling = HandlingStates.Open,
        RetainUntil = retainUntil,
    };

    private static (ITryGetSubmissionQuery Get, ISaveSubmissionCommand Save) Ports(Submission stored)
    {
        var get = Substitute.For<ITryGetSubmissionQuery>();
        var save = Substitute.For<ISaveSubmissionCommand>();
        get.ExecuteAsync(stored.Id, Arg.Any<CancellationToken>()).Returns(stored);
        return (get, save);
    }

    /// <summary>The submission as it was handed to the store - the only place a change can be observed.</summary>
    private static Submission Saved(ISaveSubmissionCommand save)
    {
        var written = save.ReceivedCalls().Single().GetArguments()[0] as Submission;
        Assert.NotNull(written);
        return written;
    }

    private static SetSubmissionRetentionUseCase RetentionOf(ITryGetSubmissionQuery get, ISaveSubmissionCommand save) =>
        new(get, save, Options.Create(TestData.Options()), TestData.Time);

    // ---- AC1: the regular deadline needs no new field ----------------------------------------------

    [Fact]
    public void GivenASubmissionWithoutAnyRetentionOverride_WhenComputingItsEffectiveExpiry_ThenItIsCreatedAtPlusRetentionDays()
    {
        var createdAt = TestData.Time.GetUtcNow().AddDays(-10);

        var expiry = SubmissionRetention.EffectiveExpiry(createdAt, null, RetentionDays);

        Assert.Equal(createdAt.AddDays(RetentionDays), expiry);
    }

    // ---- AC3: retained permanently is exempt until the exception is lifted, and AC6 records it -----

    [Fact]
    public void GivenASubmissionRetainedPermanently_WhenComputingItsEffectiveExpiry_ThenItNeverExpires()
    {
        var expiry = SubmissionRetention.EffectiveExpiry(TestData.Time.GetUtcNow(), DateTimeOffset.MaxValue, RetentionDays);

        Assert.Equal(DateTimeOffset.MaxValue, expiry);
    }

    [Fact]
    public async Task GivenAnOpenSubmission_WhenAnAdminRetainsItPermanently_ThenItIsExemptAndTheHistoryRecordsIt()
    {
        var (get, save) = Ports(Stored(TestData.Time.GetUtcNow().AddDays(-10)));

        await RetentionOf(get, save).ExecuteAsync(Id, SubmissionRetentionAction.RetainPermanently, Me);

        var s = Saved(save);
        Assert.Equal(DateTimeOffset.MaxValue, s.RetainUntil);
        var entry = Assert.Single(s.History.Entries);
        Assert.Equal(HistoryTypes.RetentionRetained, entry.Type);
        Assert.Equal(Me, entry.By);
    }

    // ---- AC4: extending shifts the deletion date by the fixed period, and AC6 records it ------------

    [Fact]
    public async Task GivenASubmission_WhenAnAdminExtendsItsRetention_ThenTheDeadlineShiftsByTheFixedPeriodAndTheHistoryRecordsIt()
    {
        var createdAt = TestData.Time.GetUtcNow();
        var (get, save) = Ports(Stored(createdAt));
        var regularDeadline = createdAt.AddDays(RetentionDays);

        await RetentionOf(get, save).ExecuteAsync(Id, SubmissionRetentionAction.Extend, Me);

        var s = Saved(save);
        Assert.Equal(regularDeadline.AddDays(365), s.RetainUntil);
        var entry = Assert.Single(s.History.Entries);
        Assert.Equal(HistoryTypes.RetentionExtended, entry.Type);
        Assert.Equal(Me, entry.By);
    }

    // ---- AC5: a retained or extended submission is never deleted by housekeeping -------------------

    /// <summary>
    /// The mutation this catches: remove (or invert) the filter RunHousekeepingUseCase must apply before
    /// deleting, since ListExpiredAsync's server-side query only ever checks CreatedAt &lt; cutoff and does
    /// not know RetainUntil exists.
    /// </summary>
    [Fact]
    public async Task GivenARetainedSubmissionPastItsRegularCutoff_WhenHousekeepingRuns_ThenItSurvives()
    {
        var (uc, list, save, delete) = HousekeepingOf();
        var retained = Stored(TestData.Time.GetUtcNow().AddDays(-200), retainUntil: DateTimeOffset.MaxValue);
        list.ListUnfinishedAsync(default, default).ReturnsForAnyArgs(Array.Empty<Submission>());
        list.ListExpiredAsync(default, default, default).ReturnsForAnyArgs(new[] { retained });

        var result = await uc.ExecuteAsync();

        Assert.Equal(0, result.Deleted);
        await delete.DidNotReceive().ExecuteAsync(retained, Arg.Any<CancellationToken>());
        _ = save;
    }

    /// <summary>
    /// The mirror of the test above: an expired submission with no override must still go. Green today -
    /// nothing in the skeleton touches this path yet - and stays green once the AC5 filter exists; the
    /// mutation that has to make it fail then is a filter broad enough to also skip this one (an inverted
    /// or an unconditional one).
    /// </summary>
    [Fact]
    public async Task GivenAnExpiredSubmissionWithoutAnyOverride_WhenHousekeepingRuns_ThenItIsStillDeleted()
    {
        var (uc, list, _, delete) = HousekeepingOf();
        var expired = Stored(TestData.Time.GetUtcNow().AddDays(-200));
        list.ListUnfinishedAsync(default, default).ReturnsForAnyArgs(Array.Empty<Submission>());
        list.ListExpiredAsync(default, default, default).ReturnsForAnyArgs(new[] { expired });

        var result = await uc.ExecuteAsync();

        Assert.Equal(1, result.Deleted);
        await delete.Received(1).ExecuteAsync(expired, Arg.Any<CancellationToken>());
    }

    // ---- AC7: lifting the exception restores the regular deadline, and AC6 records it --------------

    /// <summary>
    /// The second half of AC7 - if the regular deadline is already in the past, the next run deletes it -
    /// needs no separate test: once RetainUntil is null the submission is indistinguishable from one that
    /// was never retained, exactly the path <see cref="GivenAnExpiredSubmissionWithoutAnyOverride_WhenHousekeepingRuns_ThenItIsStillDeleted"/> proves.
    /// </summary>
    [Fact]
    public async Task GivenAPermanentlyRetainedSubmission_WhenAnAdminLiftsTheException_ThenTheRegularDeadlineAppliesAgainAndTheHistoryRecordsIt()
    {
        var (get, save) = Ports(Stored(TestData.Time.GetUtcNow().AddDays(-200), retainUntil: DateTimeOffset.MaxValue));

        await RetentionOf(get, save).ExecuteAsync(Id, SubmissionRetentionAction.Lift, Me);

        var s = Saved(save);
        Assert.Null(s.RetainUntil);
        var entry = Assert.Single(s.History.Entries);
        Assert.Equal(HistoryTypes.RetentionLifted, entry.Type);
        Assert.Equal(Me, entry.By);
    }

    // ---- Fixtures ------------------------------------------------------------------------------------

    private static (RunHousekeepingUseCase Uc, IListHousekeepingSubmissionsQuery List, ISaveSubmissionCommand Save,
        IDeleteSubmissionCommand Delete) HousekeepingOf()
    {
        var list = Substitute.For<IListHousekeepingSubmissionsQuery>();
        var save = Substitute.For<ISaveSubmissionCommand>();
        var delete = Substitute.For<IDeleteSubmissionCommand>();
        var purge = Substitute.For<IPurgeSecurityEntriesCommand>();
        purge.ExecuteAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns((0, 0));
        var listArtifacts = Substitute.For<IListArtifactsPort>();
        listArtifacts.ListAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Array.Empty<ArtifactInfo>());
        var pipeline = new SubmissionPipelineService(Array.Empty<Entriqa.Application.Pipeline.ISubmissionStep>(),
            Options.Create(TestData.Options()), TestData.Time, NullLogger<SubmissionPipelineService>.Instance);
        var uc = new RunHousekeepingUseCase(list, Substitute.For<ITryGetFormVersionQuery>(), save, delete, purge,
            Substitute.For<IRecordHousekeepingRunCommand>(), Substitute.For<IStoreArtifactPort>(), listArtifacts,
            pipeline, TestData.ConsentProofs().Service, Options.Create(TestData.Options()), TestData.Time,
            NullLogger<RunHousekeepingUseCase>.Instance);
        return (uc, list, save, delete);
    }
}
