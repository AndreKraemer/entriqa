using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Entriqa.Application.Pipeline;
using Entriqa.Application.Ports;
using Entriqa.Admin.Services;
using Entriqa.Application.UseCases;
using Entriqa.Domain.Errors;
using Entriqa.Domain.Submissions;
using Entriqa.Domain.UseCases;
using Xunit;
using DomainListItem = Entriqa.Domain.Submissions.SubmissionListItem;

namespace Entriqa.Tests;

/// <summary>
/// Issue #15: an admin can see when a submission auto-deletes and can retain it permanently, extend the
/// deadline by a fixed period, or lift that exception again - and housekeeping has to honour it. AC2 (the
/// list highlight) executes as <see cref="SubmissionRetentionDisplay"/>, a plain service class in the
/// admin project (the <see cref="SubmissionSelection"/> pattern) rather than logic in the markup - review
/// round 1 found the first version buried in a private @code method reading DateTimeOffset.UtcNow
/// directly, untestable and (confirmed by mutation) unguarded. AC8 (the consent proof from #1 is
/// untouched) is structural: nothing built here takes a dependency on ConsentProofService, so nothing
/// here can violate it.
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

    // ---- AC2: the list highlights what expires within the next 14 days ------------------------------

    private static Entriqa.Admin.Services.SubmissionListItem ExpiringItem(DateTimeOffset expiresAt, bool retainedIndefinitely = false) =>
        new("kontakt:1", "kontakt", 1, TestData.Time.GetUtcNow(), "e@example.org", "e@example.org",
            State: 3, HandlingStates.Open, null, ExpiresAt: expiresAt, RetainedIndefinitely: retainedIndefinitely);

    [Theory]
    [InlineData(1, true)]     // tomorrow
    [InlineData(14, true)]    // exactly the boundary
    [InlineData(15, false)]   // one day past the window
    [InlineData(-1, false)]   // already overdue - housekeeping's job, not a warning
    public void GivenASubmissionExpiringAtVariousOffsets_WhenCheckingTheHighlightWindow_ThenOnlyTheNext14DaysQualify(
        int daysFromNow, bool expectedHighlighted)
    {
        var now = TestData.Time.GetUtcNow();
        var item = ExpiringItem(now.AddDays(daysFromNow));

        Assert.Equal(expectedHighlighted, SubmissionRetentionDisplay.ExpiresWithinHighlightWindow(item, now));
    }

    [Fact]
    public void GivenAPermanentlyRetainedSubmission_WhenCheckingTheHighlightWindow_ThenItIsNeverHighlighted()
    {
        var now = TestData.Time.GetUtcNow();
        var item = ExpiringItem(DateTimeOffset.MaxValue, retainedIndefinitely: true);

        Assert.False(SubmissionRetentionDisplay.ExpiresWithinHighlightWindow(item, now));
    }

    [Fact]
    public void GivenASubmissionExpiringInNineDaysAndSomeHours_WhenAskedHowManyDaysRemain_ThenItRoundsUpToTen()
    {
        var now = TestData.Time.GetUtcNow();
        var item = ExpiringItem(now.AddDays(9).AddHours(3));

        Assert.Equal(10, SubmissionRetentionDisplay.DaysUntilExpiry(item, now));
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

    /// <summary>
    /// Review finding: the recorded detail (the new deadline, round-trip "O") was never read back -
    /// Labels.HistoryText ignored it and always showed the same sentence regardless of the date.
    /// </summary>
    [Fact]
    public void GivenAnExtendedRetentionHistoryEntry_WhenItIsDescribed_ThenTheGermanTextNamesTheNewDeadline()
    {
        var until = new DateTimeOffset(2027, 8, 21, 10, 0, 0, TimeSpan.Zero);
        var entry = new HistoryEntry(TestData.Time.GetUtcNow(), HistoryTypes.RetentionExtended, HistoryOrigins.Admin, Me, until.ToString("O"));

        Assert.Contains(until.ToLocalTime().ToString("dd.MM.yyyy", System.Globalization.CultureInfo.InvariantCulture),
            Labels.HistoryText(entry, new Ui()), StringComparison.Ordinal);
    }

    /// <summary>
    /// Review finding: DateTimeOffset.MaxValue has no "365 days later" - extending an already-permanent
    /// submission threw ArgumentOutOfRangeException instead of a clean validation error. Nothing in the
    /// admin UI offers the button in this state, but the endpoint itself has no such guard.
    /// </summary>
    [Fact]
    public async Task GivenAPermanentlyRetainedSubmission_WhenAnAdminExtendsItAnyway_ThenItIsRejectedAsInvalidRatherThanThrowing()
    {
        var (get, save) = Ports(Stored(TestData.Time.GetUtcNow(), retainUntil: DateTimeOffset.MaxValue));

        var ex = await Assert.ThrowsAsync<AppException>(
            () => RetentionOf(get, save).ExecuteAsync(Id, SubmissionRetentionAction.Extend, Me));

        Assert.Equal(ErrorMessages.RetentionAlreadyPermanent, ex.MessageKey);
        Assert.Empty(save.ReceivedCalls());
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

    // ---- AC1: the detail view and the inbox queries surface the effective deadline ------------------

    [Fact]
    public async Task GivenASubmissionRetainedPermanently_WhenTheDetailViewIsBuilt_ThenItReportsRetainedIndefinitely()
    {
        var get = Substitute.For<ITryGetSubmissionQuery>();
        get.ExecuteAsync(Id, Arg.Any<CancellationToken>())
            .Returns(Stored(TestData.Time.GetUtcNow().AddDays(-10), retainUntil: DateTimeOffset.MaxValue));
        var uc = new GetSubmissionDetailUseCase(get, Substitute.For<ITryGetFormVersionQuery>(), Options.Create(TestData.Options()));

        var view = await uc.ExecuteAsync(Id);

        Assert.True(view.RetainedIndefinitely);
        Assert.True(view.HasRetentionOverride);
        Assert.Equal(DateTimeOffset.MaxValue, view.ExpiresAt);
    }

    /// <summary>
    /// Review finding: HasRetentionOverride decides whether "Ausnahme aufheben" is offered at all (AC3/AC7),
    /// and it is what tells an extended-but-not-permanent submission apart from one that was never touched -
    /// nothing exercised it before this.
    /// </summary>
    [Fact]
    public async Task GivenASubmissionExtendedButNotPermanent_WhenTheDetailViewIsBuilt_ThenItReportsAnOverrideWithoutIndefiniteRetention()
    {
        var extendedUntil = TestData.Time.GetUtcNow().AddDays(365);
        var get = Substitute.For<ITryGetSubmissionQuery>();
        get.ExecuteAsync(Id, Arg.Any<CancellationToken>())
            .Returns(Stored(TestData.Time.GetUtcNow(), retainUntil: extendedUntil));
        var uc = new GetSubmissionDetailUseCase(get, Substitute.For<ITryGetFormVersionQuery>(), Options.Create(TestData.Options()));

        var view = await uc.ExecuteAsync(Id);

        Assert.True(view.HasRetentionOverride);
        Assert.False(view.RetainedIndefinitely);
        Assert.Equal(extendedUntil, view.ExpiresAt);
    }

    [Fact]
    public async Task GivenASubmissionWithoutAnyRetentionOverride_WhenTheDetailViewIsBuilt_ThenItReportsNoOverride()
    {
        var get = Substitute.For<ITryGetSubmissionQuery>();
        get.ExecuteAsync(Id, Arg.Any<CancellationToken>()).Returns(Stored(TestData.Time.GetUtcNow()));
        var uc = new GetSubmissionDetailUseCase(get, Substitute.For<ITryGetFormVersionQuery>(), Options.Create(TestData.Options()));

        var view = await uc.ExecuteAsync(Id);

        Assert.False(view.HasRetentionOverride);
        Assert.False(view.RetainedIndefinitely);
    }

    // ---- SubmissionRetention.Project: the pure function the inbox queries delegate to ---------------

    /// <summary>
    /// Review finding: the source guard below only checks that the queries call Project by name - a
    /// mutation gutting Project's own body left the whole suite green. This exercises the function itself.
    /// </summary>
    [Fact]
    public void GivenARowWithoutAnyOverride_WhenProjected_ThenItGetsTheRegularExpiryAndNoOverrideFlags()
    {
        var createdAt = TestData.Time.GetUtcNow().AddDays(-10);
        var item = new DomainListItem("kontakt:1", "kontakt", 1, createdAt, "e@example.org", "e@example.org",
            SubmissionState.Done, HandlingStates.Open, null);

        var projected = SubmissionRetention.Project(item, null, RetentionDays);

        Assert.Equal(createdAt.AddDays(RetentionDays), projected.ExpiresAt);
        Assert.False(projected.RetainedIndefinitely);
        Assert.False(projected.HasRetentionOverride);
    }

    [Fact]
    public void GivenARowRetainedPermanently_WhenProjected_ThenItGetsIndefiniteRetention()
    {
        var item = new DomainListItem("kontakt:1", "kontakt", 1, TestData.Time.GetUtcNow(), "e@example.org", "e@example.org",
            SubmissionState.Done, HandlingStates.Open, null);

        var projected = SubmissionRetention.Project(item, DateTimeOffset.MaxValue, RetentionDays);

        Assert.Equal(DateTimeOffset.MaxValue, projected.ExpiresAt);
        Assert.True(projected.RetainedIndefinitely);
        Assert.True(projected.HasRetentionOverride);
    }

    [Fact]
    public void GivenARowExtendedButNotPermanent_WhenProjected_ThenItGetsAnOverrideWithoutIndefiniteRetention()
    {
        var until = TestData.Time.GetUtcNow().AddDays(365);
        var item = new DomainListItem("kontakt:1", "kontakt", 1, TestData.Time.GetUtcNow(), "e@example.org", "e@example.org",
            SubmissionState.Done, HandlingStates.Open, null);

        var projected = SubmissionRetention.Project(item, until, RetentionDays);

        Assert.Equal(until, projected.ExpiresAt);
        Assert.False(projected.RetainedIndefinitely);
        Assert.True(projected.HasRetentionOverride);
    }

    /// <summary>
    /// The customs post, both directions (precedent: SubmissionHistoryTests' history round-trip guard).
    /// Entriqa.Data has no integration tests and its entities are internal, so nothing here can call the
    /// mapper - and with no guard at all, dropping RetainUntil from either direction leaves the whole
    /// suite green while "retain permanently" either never persists or never comes back. Review finding:
    /// deleting the ToEntity line survived the whole suite.
    /// </summary>
    [Fact]
    public void GivenASubmissionWithARetentionOverride_WhenItIsStored_ThenItTravelsInTheSubmissionRowInBothDirections()
    {
        var source = File.ReadAllText(Path.Combine(SourceText.RepoDirectory("src", "Core", "Entriqa.Data"), "Mapping", "SubmissionMapper.cs"));

        Assert.Contains("RetainUntil = s.RetainUntil,", SourceText.Block(source, "public static SubmissionEntity ToEntity",
            "SubmissionMapper no longer has ToEntity - update this guard."), StringComparison.Ordinal);
        Assert.Contains("RetainUntil = e.RetainUntil,", SourceText.Block(source, "public static Submission ToDomain",
            "SubmissionMapper no longer has ToDomain - update this guard."), StringComparison.Ordinal);
    }

    /// <summary>
    /// The two queries behind the inbox (#12: search, and the default listing) read entities straight from
    /// Table Storage, which this test host cannot exercise - no Azurite here. The wiring is read from
    /// source instead, precedent <see cref="SubmissionHistoryTests"/>'s data-layer guards.
    /// </summary>
    [Theory]
    [InlineData("AdminOverviewQueries.cs", "class ListRecentSubmissionsQuery")]
    [InlineData("SubmissionQueries.cs", "class SearchSubmissionsQuery")]
    [InlineData("SubmissionQueries.cs", "class ListSubmissionsQuery")]
    public void GivenAnInboxQuery_WhenItMapsARow_ThenItProjectsTheEffectiveRetentionOntoIt(string file, string marker)
    {
        var source = File.ReadAllText(Path.Combine(SourceText.RepoDirectory("src", "Core", "Entriqa.Data"), "Queries", file));

        Assert.Contains("SubmissionRetention.Project(", SourceText.Block(source, marker,
            $"{marker} is gone - update this guard."), StringComparison.Ordinal);
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

    // ---- The detail panel's buttons - no render host, so read from source (admin-ui skill) -----------

    /// <summary>
    /// Review finding: swapping the "lift" button's handler for "retain" left the whole suite green - the
    /// panel has no other runtime surface. Each handler bound to exactly one button catches a swap either
    /// way: the removed handler's count drops to zero, the duplicated one's count rises to two.
    /// </summary>
    [Theory]
    [InlineData("RetainPermanently")]
    [InlineData("ExtendRetention")]
    [InlineData("LiftRetention")]
    public void GivenTheRetentionPanel_WhenItsButtonsAreRead_ThenEachActionIsWiredToExactlyOneButton(string handler)
    {
        var markup = AdminMarkup.Read("Components", "SubmissionDetailPanel.razor");

        Assert.Single(Regex.Matches(markup, $@"@onclick=""{handler}"""));
    }

    // ---- The endpoint's wire mapping - the only translation between the client and the use case -------

    /// <summary>
    /// Review finding: the endpoint's "retain"/"extend"/"lift" -&gt; enum mapping had no guard, so swapping
    /// two branches (e.g. "lift" -&gt; RetainPermanently) left the whole suite green while the button would
    /// silently do the wrong thing.
    /// </summary>
    [Theory]
    [InlineData("\"retain\"", "SubmissionRetentionAction.RetainPermanently")]
    [InlineData("\"extend\"", "SubmissionRetentionAction.Extend")]
    [InlineData("\"lift\"", "SubmissionRetentionAction.Lift")]
    public void GivenTheRetentionEndpoint_WhenItMapsTheWireAction_ThenEachStringReachesItsOwnEnumValue(string wireValue, string enumValue)
    {
        var body = Endpoint("AdminSetRetention");

        Assert.Matches(new Regex($@"{Regex.Escape(wireValue)}\s*=>\s*{Regex.Escape(enumValue)}"), body);
    }

    /// <summary>One Function, from its attribute to the next one (precedent: SubmissionHistoryTests.Endpoint).</summary>
    private static string Endpoint(string name)
    {
        var source = File.ReadAllText(Path.Combine(SourceText.RepoDirectory("src", "Hosts", "Entriqa.Functions"), "Admin", "AdminFunctions.cs"));
        var start = source.IndexOf($"[Function(\"{name}\")]", StringComparison.Ordinal);
        Assert.True(start > 0, $"AdminFunctions no longer has {name} - update this guard.");
        var end = source.IndexOf("[Function(", start + 10, StringComparison.Ordinal);
        return end > start ? source[start..end] : source[start..];
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
