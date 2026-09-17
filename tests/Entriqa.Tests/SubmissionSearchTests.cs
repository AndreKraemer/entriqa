using System.Text.RegularExpressions;
using Entriqa.Admin.Services;
using Entriqa.Application.Ports;
using Entriqa.Application.UseCases;
using Entriqa.Domain.Submissions;
using NSubstitute;
using Xunit;
using AdminItem = Entriqa.Admin.Services.SubmissionListItem;
using DomainItem = Entriqa.Domain.Submissions.SubmissionListItem;

namespace Entriqa.Tests;

/// <summary>
/// Issue #12: the inbox gets a search across e-mail, name and every field value, plus a date range,
/// and the four narrowings (Zeitraum, Verarbeitungsstatus, Bearbeitungsstatus, Bearbeiter) have to
/// combine freely.
///
/// The work splits along the one line that decides what a test can reach. The match runs in
/// <see cref="SearchSubmissionsUseCase"/>, above the port, so which term finds what is executed here
/// rather than read as source. Everything the reader then narrows further happens on the list that is
/// already in the browser, which is <see cref="SubmissionSelection"/> - also executed. Only the scan
/// itself, the endpoint and the markup are out of reach of this host, and those get source guards at
/// the bottom of this file.
/// </summary>
public class SubmissionSearchTests
{
    // ---- Fixtures ----

    /// <summary>
    /// Noon UTC so that a day boundary is nowhere near the fixture; the range tests pass
    /// <see cref="TimeZoneInfo.Utc"/> explicitly, because a selection that compared the machine's zone
    /// would pass or fail depending on where the suite runs.
    /// </summary>
    private static readonly DateTimeOffset Noon = new(2026, 8, 20, 12, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// Five submissions varying all four narrowings at once - that is what AC4 needs and what neither
    /// AdminSubmissionList (one clock hour apart, no assignees) nor AssignedSubmissionList (all at the
    /// same instant, all in one state) can offer. Local to this file on purpose: both shared fixtures
    /// have tests counting on their exact distribution.
    /// </summary>
    private static List<AdminItem> Mixed() =>
    [
        Row("a", daysAgo: 0, state: 2, handling: "open", assignee: TestData.AdminMe),
        Row("b", daysAgo: 0, state: 1, handling: "open", assignee: TestData.AdminMe),
        Row("c", daysAgo: 1, state: 2, handling: "done", assignee: TestData.AdminMe),
        Row("d", daysAgo: 2, state: 3, handling: "open", assignee: TestData.AdminColleague),
        Row("e", daysAgo: 5, state: 2, handling: "open", assignee: null),
    ];

    private static AdminItem Row(string id, int daysAgo, int state, string handling, string? assignee) =>
        new(id, "kontakt", 1, Noon.AddDays(-daysAgo), $"{id}@example.org", $"Person {id}", state, handling, null, assignee);

    private static string[] Ids(IEnumerable<AdminItem> items) => items.Select(i => i.Id).ToArray();

    private static string[] Ids(IEnumerable<DomainItem> items) => items.Select(i => i.Id).ToArray();

    private static string[] Selected(SubmissionSelection selection) =>
        Ids(selection.Select(Mixed(), lastVisit: null, TimeZoneInfo.Utc));

    private static DateOnly DayOf(int daysAgo) => DateOnly.FromDateTime(Noon.AddDays(-daysAgo).UtcDateTime);

    private static SubmissionCandidate Candidate(string id, int daysAgo, params string[] texts) =>
        new(new DomainItem(id, "kontakt", 1, Noon.AddDays(-daysAgo), null, id, SubmissionState.Done, "open", null), texts);

    private static SearchSubmissionsUseCase Search(
        out ISearchSubmissionsQuery query, DateTimeOffset? lastVisit = null, int scanned = 3, bool capped = false,
        params SubmissionCandidate[] candidates)
    {
        query = Substitute.For<ISearchSubmissionsQuery>();
        query.ExecuteAsync(Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new SubmissionCandidates(candidates, scanned, capped));
        var visit = Substitute.For<IGetLastVisitQuery>();
        visit.ExecuteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(lastVisit);
        return new SearchSubmissionsUseCase(query, visit);
    }

    // ---- The match itself (AC1, AC2, AC3) ----

    /// <summary>
    /// AC1. The term is looked for in the candidate's texts as a substring, and a candidate that does not
    /// carry it stays out - both halves in one test, because an implementation that returns everything and
    /// one that returns nothing are equally wrong.
    /// </summary>
    [Fact]
    public async Task GivenACandidateWhoseTextCarriesTheTerm_WhenSearching_ThenOnlyItIsInTheResult()
    {
        var useCase = Search(out _, candidates:
        [
            Candidate("hit", 0, "kim@example.org", "Rückruf für die Odysys AG erbeten."),
            Candidate("miss", 1, "robin@example.org", "Bitte um Rückruf zum Angebot."),
        ]);

        var result = await useCase.ExecuteAsync(null, "Odysys", TestData.AdminMe);

        Assert.Equal(["hit"], Ids(result.Items));
    }

    /// <summary>AC2 - and the direction that matters, since the stored text is what varies in practice.</summary>
    [Theory]
    [InlineData("odysys")]
    [InlineData("ODYSYS")]
    [InlineData("OdYsYs")]
    public async Task GivenATermInAnotherCase_WhenSearching_ThenTheMatchIsFoundAnyway(string term)
    {
        var useCase = Search(out _, candidates: [Candidate("hit", 0, "Rückruf für die Odysys AG erbeten.")]);

        Assert.Equal(["hit"], Ids((await useCase.ExecuteAsync(null, term, TestData.AdminMe)).Items));
    }

    /// <summary>
    /// AC3, both directions. The narrowing has to reach the scan rather than being applied to its result -
    /// scanning every form and then dropping four fifths would spend the ceiling on rows the reader
    /// excluded, and the count AC5 shows would be about a set they never asked for.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("kontakt")]
    public async Task GivenAFormOrNone_WhenSearching_ThenTheScanIsNarrowedToExactlyThat(string? slug)
    {
        var useCase = Search(out var query, candidates: []);

        await useCase.ExecuteAsync(slug, "odysys", TestData.AdminMe, scanMax: 250);

        // The ceiling is asserted here rather than waved through with Arg.Any: it arrives from the endpoint,
        // where it is clamped, and a use case that substituted one of its own would make that clamp a lie.
        await query.Received(1).ExecuteAsync(slug, 250, Arg.Any<CancellationToken>());
    }

    /// <summary>AC5 and AC6: both facts come from the scan and must survive the use case unchanged.</summary>
    [Fact]
    public async Task GivenTheScanHitItsCeiling_WhenSearching_ThenTheResultSaysSoAndHowManyWereSearched()
    {
        var useCase = Search(out _, scanned: 5000, capped: true, candidates: [Candidate("hit", 0, "Odysys")]);

        var result = await useCase.ExecuteAsync(null, "odysys", TestData.AdminMe);

        Assert.Equal(5000, result.Scanned);
        Assert.True(result.Capped);
    }

    /// <summary>The blue "new since" dot has to work in a hit list too, so the visit travels with it.</summary>
    [Fact]
    public async Task GivenTheCallersLastVisit_WhenSearching_ThenItTravelsWithTheHits()
    {
        var useCase = Search(out _, lastVisit: Noon, candidates: [Candidate("hit", 0, "Odysys")]);

        Assert.Equal(Noon, (await useCase.ExecuteAsync(null, "odysys", TestData.AdminMe)).LastVisitAt);
    }

    /// <summary>
    /// The list the admin pages through is newest first everywhere else, and the neighbours of the detail
    /// view follow the order they are handed - a scan that returns partitions in key order would otherwise
    /// put whitepaper before kontakt and call it recency.
    /// </summary>
    [Fact]
    public async Task GivenMatchesFromSeveralDays_WhenSearching_ThenTheHitsAreNewestFirst()
    {
        var useCase = Search(out _, candidates:
        [
            Candidate("older", 5, "Odysys"),
            Candidate("newest", 0, "Odysys"),
            Candidate("middle", 2, "Odysys"),
        ]);

        Assert.Equal(["newest", "middle", "older"], Ids((await useCase.ExecuteAsync(null, "odysys", TestData.AdminMe)).Items));
    }

    /// <summary>
    /// A term of nothing but space is what an unlucky paste leaves behind. Matching it as a substring would
    /// return the whole scan and call it a hit list - so the term is a single space, which the fixture text
    /// does contain. Three of them would miss on their own and the test would pass without any trimming.
    /// </summary>
    [Fact]
    public async Task GivenATermOfOnlyWhitespace_WhenSearching_ThenNothingMatches()
    {
        var useCase = Search(out _, candidates: [Candidate("hit", 0, "Rückruf für die Odysys AG erbeten.")]);

        Assert.Empty((await useCase.ExecuteAsync(null, " ", TestData.AdminMe)).Items);
    }

    // ---- Combining the narrowings (AC4) ----

    /// <summary>
    /// AC4 across two dimensions: "zu bearbeiten" and "Fehler" used to exclude each other, because both
    /// lived in the same slot. Together they now mean open *and* failed.
    /// </summary>
    [Fact]
    public void GivenChipsOfTwoDimensions_WhenSelecting_ThenOnlySubmissionsMatchingBothRemain() =>
        Assert.Equal(["a", "e"], Selected(new SubmissionSelection(null, "failed,todo", 1)));

    /// <summary>
    /// Two chips of the *same* dimension are the one place AC4's "alle Bedingungen zugleich" cannot be
    /// taken literally: no submission is waiting and failed at once, so the literal reading makes the
    /// second click empty the list. Within a dimension the chips therefore widen.
    /// </summary>
    [Fact]
    public void GivenTwoChipsOfTheSameDimension_WhenSelecting_ThenSubmissionsOfEitherRemain() =>
        Assert.Equal(["a", "b", "c", "e"], Selected(new SubmissionSelection(null, "failed,waiting", 1)));

    /// <summary>AC4 with all four narrowings set at once - the criterion's actual claim.</summary>
    [Fact]
    public void GivenARangeAStateAHandlingAndAnAssignee_WhenSelecting_ThenAllFourNarrowTogether() =>
        Assert.Equal(["a"], Selected(new SubmissionSelection(
            null, "failed,todo", 1, TestData.AdminMe, Search: null, From: DayOf(1), To: DayOf(0))));

    /// <summary>The range takes both its days with it, or the reader's last day silently drops out.</summary>
    [Fact]
    public void GivenARangeOfTwoDays_WhenSelecting_ThenBothItsEndsAreIncluded() =>
        Assert.Equal(["c", "d"], Selected(new SubmissionSelection(null, null, 1, null, null, DayOf(2), DayOf(1))));

    /// <summary>
    /// The range is about the day the list prints beside a submission, which is the reader's day. An
    /// enquiry that arrives at half past midnight in Berlin belongs to that day - judged in UTC it falls
    /// into the one before, and drops out of a range that names it.
    /// </summary>
    [Fact]
    public void GivenASubmissionWhoseLocalDayIsNotItsUtcDay_WhenSelectingThatLocalDay_ThenItIsInTheRange()
    {
        var berlin = TimeZoneInfo.CreateCustomTimeZone("test-berlin", TimeSpan.FromHours(2), "test", "test");
        var item = new AdminItem("late", "kontakt", 1, new DateTimeOffset(2026, 8, 20, 22, 30, 0, TimeSpan.Zero),
            null, "Person late", 3, "open", null);
        var localDay = new DateOnly(2026, 8, 21);

        var selection = new SubmissionSelection(null, null, 1, null, null, localDay, localDay);

        Assert.Equal(["late"], Ids(selection.Select([item], lastVisit: null, berlin)));
    }

    /// <summary>Each end works on its own; an open end never narrows.</summary>
    [Theory]
    [InlineData(1, null, new[] { "a", "b", "c" })]
    [InlineData(null, 1, new[] { "c", "d", "e" })]
    public void GivenOnlyOneEndOfTheRange_WhenSelecting_ThenEverythingOnThatSideRemains(
        int? fromDaysAgo, int? toDaysAgo, string[] expected) =>
        Assert.Equal(expected, Selected(new SubmissionSelection(null, null, 1, null, null,
            fromDaysAgo is { } f ? DayOf(f) : null, toDaysAgo is { } t ? DayOf(t) : null)));

    // ---- The address carries search and range ----

    [Fact]
    public void GivenASelectionWithATermAndARange_WhenTurningItIntoAnAddressAndBack_ThenItIsUnchanged()
    {
        var selection = new SubmissionSelection("kontakt", "failed,todo", 2, TestData.AdminMe,
            "Odysys AG", new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 20));
        var url = selection.DetailUrl("s07");

        Assert.Equal(selection, SubmissionSelection.FromQuery(null, url[url.IndexOf('?', StringComparison.Ordinal)..]));
    }

    /// <summary>
    /// The chips are a set, and a set has no order - but the selection is a record, so two addresses
    /// naming the same chips have to produce equal values or every comparison against it breaks quietly:
    /// the lit chip, the badge count and the way back all key on equality.
    /// </summary>
    [Fact]
    public void GivenTheSameChipsInEitherOrder_WhenReadingThemBack_ThenTheSelectionsAreEqual()
    {
        Assert.Equal(SubmissionSelection.FromQuery(null, "?filter=todo,failed"),
                     SubmissionSelection.FromQuery(null, "?filter=failed,todo"));

        // The three ways a selection comes into being, because they do not share one line of code: the
        // address, the constructor's field initialiser, and the init accessor a with-expression takes.
        Assert.Equal(new SubmissionSelection(null, "todo,failed", 1), new SubmissionSelection(null, "failed,todo", 1));
        Assert.Equal(SubmissionSelection.Default with { Quick = "todo,failed" },
                     SubmissionSelection.Default with { Quick = "failed,todo" });
    }

    /// <summary>A hand-edited address must not smuggle a narrowing the list cannot apply.</summary>
    [Fact]
    public void GivenAnAddressNamingAnUnknownChipBesideAKnownOne_WhenReadingItBack_ThenOnlyTheKnownOneApplies() =>
        Assert.Equal("todo", SubmissionSelection.FromQuery(null, "?filter=todo,erledigt").Quick);

    [Fact]
    public void GivenAChipIsNotSet_WhenTogglingIt_ThenItJoinsTheOthers()
    {
        var selection = new SubmissionSelection(null, "todo", 3).ToggleChip("failed");

        Assert.True(selection.HasChip("todo"));
        Assert.True(selection.HasChip("failed"));
        Assert.Equal(1, selection.Page);
    }

    [Fact]
    public void GivenAChipIsSet_WhenTogglingIt_ThenItGoesAndTheOthersStay()
    {
        var selection = new SubmissionSelection(null, "failed,todo", 1).ToggleChip("failed");

        Assert.False(selection.HasChip("failed"));
        Assert.True(selection.HasChip("todo"));
    }

    // ---- What costs a request, and what does not (AC8, AC3) ----

    /// <summary>
    /// AC8 from the side that can actually be wrong. The term is the only narrowing the endpoint applies,
    /// so clearing it has to fetch the plain inbox back - a page that kept the hit list and merely stopped
    /// highlighting would show a permanently narrowed list with an empty search box.
    /// </summary>
    [Theory]
    [InlineData(null, "Odysys")]
    [InlineData("Odysys", null)]
    [InlineData("Odysys", "Odyssey")]
    public void GivenTheTermChanged_WhenAskingWhetherToReload_ThenItReloads(string? before, string? after) =>
        Assert.True(new SubmissionSelection(null, null, 1, null, after)
            .NeedsReload(new SubmissionSelection(null, null, 1, null, before)));

    [Fact]
    public void GivenAnotherFormWasChosen_WhenAskingWhetherToReload_ThenItReloads() =>
        Assert.True(new SubmissionSelection("whitepaper", null, 1).NeedsReload(new SubmissionSelection("kontakt", null, 1)));

    /// <summary>
    /// Everything else narrows what is already here. Fetching again on a chip or a page turn would make
    /// every click cost a scan, and #11's whole list model exists to avoid exactly that.
    /// </summary>
    [Fact]
    public void GivenOnlyTheOtherNarrowingsChanged_WhenAskingWhetherToReload_ThenItDoesNot() =>
        Assert.False(new SubmissionSelection("kontakt", "failed,todo", 4, TestData.AdminMe, "Odysys", DayOf(2), DayOf(0))
            .NeedsReload(new SubmissionSelection("kontakt", null, 1, null, "Odysys")));

    // ---- What the list says about its search (AC5, AC6, AC7) ----

    [Fact]
    public void GivenNoSearchIsRunning_WhenAskingForTheSearchSummary_ThenThereIsNone() =>
        Assert.Null(new SubmissionSelection(null, null, 1).SearchSummary(new Ui(), 120, capped: false));

    [Fact]
    public void GivenASearchOverEveryStoredSubmission_WhenAskingForTheSummary_ThenItNamesHowManyWereSearched() =>
        Assert.Contains("120", Term("Odysys").SearchSummary(new Ui(), 120, capped: false), StringComparison.Ordinal);

    /// <summary>
    /// AC6. Asserted as a difference rather than against a wording, so the guard survives an edit of the
    /// sentence: what must never happen is a capped result that reads exactly like a complete one.
    /// </summary>
    [Fact]
    public void GivenTheCeilingWasReached_WhenAskingForTheSummary_ThenItDiffersFromACompleteOne()
    {
        var selection = Term("Odysys");

        var capped = selection.SearchSummary(new Ui(), 5000, capped: true);

        Assert.Contains("5000", capped!, StringComparison.Ordinal);
        Assert.NotEqual(selection.SearchSummary(new Ui(), 5000, capped: false), capped);
    }

    /// <summary>AC7: the reader has to see which term found nothing, not that something found nothing.</summary>
    [Fact]
    public void GivenASearchWithoutHits_WhenAskingForTheEmptyMessage_ThenItNamesTheTerm() =>
        Assert.Contains("Odysys AG", Term("Odysys AG").EmptyMessage(new Ui()), StringComparison.Ordinal);

    [Fact]
    public void GivenNoSearchIsRunning_WhenAskingForTheEmptyMessage_ThenItIsTheOrdinaryEmptyText()
    {
        var t = new Ui();

        Assert.Equal(t["Nichts in dieser Auswahl."], new SubmissionSelection(null, null, 1).EmptyMessage(t));
    }

    /// <summary>
    /// The German strings are the translation keys, so a new one without an English entry falls back to
    /// German and nobody notices until an English reader does.
    /// </summary>
    [Fact]
    public void GivenTheEnglishInterface_WhenAskingForTheSearchMessages_ThenBothAreTranslated()
    {
        var en = new Ui();
        en.Init("en");
        var selection = Term("Odysys");

        Assert.NotEqual(selection.SearchSummary(new Ui(), 120, capped: false), selection.SearchSummary(en, 120, capped: false));
        Assert.NotEqual(selection.EmptyMessage(new Ui()), selection.EmptyMessage(en));
    }

    private static SubmissionSelection Term(string term) => new(null, null, 1, null, term);

    // ---- Guards for the three layers this host cannot execute ----

    /// <summary>
    /// Which texts a term may match is decided in the mapper, where nothing here can call it -
    /// Entriqa.Data has no integration tests and its entities are internal. Without this guard, dropping
    /// the field values leaves every test above green while the issue's own scenario - finding a company
    /// name that only exists in a free-text answer - is dead.
    /// </summary>
    [Fact]
    public void GivenTheSearchCandidateMapping_WhenReadingIt_ThenEmailNameAndEveryFieldValueAreSearchable()
    {
        var body = SourceText.Block(DataSource("Mapping", "SubmissionMapper.cs"),
            "public static SubmissionCandidate ToCandidate",
            "SubmissionMapper has no ToCandidate - update this guard.");

        Assert.Matches(new Regex(@"^[^\S\r\n]*[^/\r\n]*e\.Email", RegexOptions.Multiline), body);
        Assert.Matches(new Regex(@"^[^\S\r\n]*[^/\r\n]*e\.FirstName", RegexOptions.Multiline), body);
        Assert.Matches(new Regex(@"values\.Values", RegexOptions.Multiline), body);
    }

    /// <summary>
    /// The scan behind the search. Both halves have to be there: the partition narrowing is AC3's server
    /// side, and the ceiling is what AC6 reports - a scan that ignored scanMax would report a cap that
    /// never happened, or never report one that did.
    /// </summary>
    [Fact]
    public void GivenTheSearchScan_WhenReadingIt_ThenItNarrowsByFormAndStopsAtTheCeiling()
    {
        var body = SourceText.Block(DataSource("Queries", "SubmissionQueries.cs"),
            "class SearchSubmissionsQuery",
            "SubmissionQueries has no SearchSubmissionsQuery - update this guard.");

        Assert.Contains("PartitionKey == slug", body, StringComparison.Ordinal);
        // The comparison, not the name: "scanMax" alone is satisfied by the signature the block starts with.
        Assert.Contains(">= scanMax", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// The endpoint. Entriqa.Functions is deliberately unreferenced here (the worker SDK does not load in
    /// this test host), so the one line that decides whether a term is answered by a search at all can
    /// only be read.
    /// </summary>
    [Fact]
    public void GivenTheInboxEndpoint_WhenReadingIt_ThenATermIsAnsweredByTheSearchAndTheCeilingIsClamped()
    {
        var body = SourceText.Block(
            File.ReadAllText(Path.Combine(SourceText.RepoDirectory("src", "Hosts", "Entriqa.Functions"), "Admin", "AdminFunctions.cs")),
            "public async Task<IActionResult> Recent",
            "AdminFunctions no longer has Recent - update this guard.");

        Assert.Contains("\"q\"", body, StringComparison.Ordinal);
        Assert.Contains("search.ExecuteAsync", body, StringComparison.Ordinal);
        Assert.Contains("Math.Clamp(n, 1, 5000)", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// The chips have to go through ToggleChip. Left as the assignment they are today, the four would keep
    /// replacing one another - AC4's combination would be unreachable in the interface while every test
    /// above this line stayed green, which is precisely how #11's dead quick filters passed 186 tests.
    /// </summary>
    [Fact]
    public void GivenTheInbox_WhenReadingItsChips_ThenTheyToggleIndependently()
    {
        // The chip itself, not the file: the same two names appear in the badge count above it, so a whole
        // file scan is satisfied while the button goes back to replacing one chip with the next.
        var button = SourceText.Block(AdminMarkup.Read("Pages", "Submissions.razor"),
            "private RenderFragment QuickButton", "the inbox no longer has QuickButton - update this guard.");

        Assert.Contains("_selection.HasChip(key)", button, StringComparison.Ordinal);
        Assert.Contains("_selection.ToggleChip(key)", button, StringComparison.Ordinal);
        Assert.DoesNotContain("Quick =", button, StringComparison.Ordinal);
    }

    /// <summary>The search box and the two date inputs are state, so they belong in the address (#11).</summary>
    [Fact]
    public void GivenTheInbox_WhenReadingItsFilterBar_ThenTheTermAndTheRangeReachTheAddress()
    {
        var bar = AdminMarkup.Between(AdminMarkup.Read("Pages", "Submissions.razor"),
            "<div class=\"filters\">", "</table>", "the inbox no longer has a filters bar - update this guard.");

        Assert.Contains("type=\"search\"", bar, StringComparison.Ordinal);
        Assert.Contains("type=\"date\"", bar, StringComparison.Ordinal);
        Assert.Contains("Search =", bar, StringComparison.Ordinal);
        Assert.Contains("From =", bar, StringComparison.Ordinal);
        Assert.Contains("To =", bar, StringComparison.Ordinal);
    }

    /// <summary>
    /// AC5, AC6 and AC7 are sentences the selection composes and the page renders. The composition is
    /// executed above; that it reaches the screen at all is only readable here.
    /// </summary>
    [Fact]
    public void GivenTheInbox_WhenReadingIt_ThenItRendersTheSearchSummaryAndTheEmptyMessage()
    {
        var markup = AdminMarkup.Read("Pages", "Submissions.razor");
        var refresh = SourceText.Block(markup, "private async Task Refresh",
            "the inbox no longer has Refresh - update this guard.");

        // What the scan cost has to reach the fields the summary reads. Hard-coding them away leaves the
        // bar reading "0 Einsendungen durchsucht" and never reporting a ceiling - AC5 and AC6 both dead.
        Assert.Contains("hits.Scanned", refresh, StringComparison.Ordinal);
        Assert.Contains("hits.Capped", refresh, StringComparison.Ordinal);

        // And the line is rendered on nothing but the summary being there. Any further condition can hide
        // a capped result, which is exactly what AC6 forbids.
        Assert.Contains("@if (_selection.SearchSummary(T, _scanned, _capped) is { } summary)", markup,
            StringComparison.Ordinal);
        Assert.Contains("EmptyMessage", markup, StringComparison.Ordinal);
    }

    /// <summary>
    /// The two handlers that rebuild or compare a selection instead of deriving one, which is where the
    /// chip set and the new address state each left a hole. The form picker has to keep every narrowing
    /// the reader set - dropping the term empties the search box mid-search - and marking everything as
    /// seen has to put out the "new" chip through the set, or the comparison is false the moment "new" is
    /// combined with another chip and the list stays narrowed to nothing.
    /// </summary>
    [Fact]
    public void GivenTheInbox_WhenReadingItsHandlers_ThenTheyDeriveTheSelectionInsteadOfRebuildingIt()
    {
        var markup = AdminMarkup.Read("Pages", "Submissions.razor");

        var slugChange = AdminMarkup.Between(markup, "private void OnSlugChange", ";",
            "the inbox no longer has OnSlugChange - update this guard.");
        Assert.Contains("_selection with", slugChange, StringComparison.Ordinal);
        Assert.DoesNotContain("new SubmissionSelection", slugChange, StringComparison.Ordinal);

        var markVisited = SourceText.Block(markup, "private async Task MarkVisited",
            "the inbox no longer has MarkVisited - update this guard.");
        Assert.Contains("HasChip(\"new\")", markVisited, StringComparison.Ordinal);
        Assert.DoesNotContain("Quick ==", markVisited, StringComparison.Ordinal);
    }

    /// <summary>
    /// The page must ask NeedsReload rather than comparing the slug itself, or a changed term would
    /// silently keep the previous list - AC8 with an empty search box and a narrowed list.
    /// </summary>
    [Fact]
    public void GivenTheInbox_WhenReadingHowItAppliesTheAddress_ThenItReloadsOnWhatTheSelectionSays()
    {
        var body = SourceText.Block(AdminMarkup.Read("Pages", "Submissions.razor"),
            "private async Task ApplyAddress", "the inbox no longer has ApplyAddress - update this guard.");

        Assert.Contains("NeedsReload", body, StringComparison.Ordinal);
    }

    private static string DataSource(params string[] relative) =>
        File.ReadAllText(Path.Combine([SourceText.RepoDirectory("src", "Core", "Entriqa.Data"), .. relative]));
}
