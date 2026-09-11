using Entriqa.Admin.Services;
using Xunit;

namespace Entriqa.Tests;

/// <summary>
/// Issue #11: the submission detail moves onto its own full-width page, so the list state the reader
/// came from can no longer live in a field of the list component - it has to travel in the address.
/// <see cref="SubmissionSelection"/> is that state, and everything the two pages do with it (the way
/// back, its label, walking the neighbours) is decided here rather than in Razor markup.
/// </summary>
public class SubmissionSelectionTests
{
    private static IReadOnlyList<SubmissionListItem> Kontakt(SubmissionSelection selection) =>
        selection.Select(TestData.AdminSubmissionList(), lastVisit: null);

    // ---- The address carries the selection (AC2, AC6) ----

    [Fact]
    public void GivenASelectionWithFormFilterAndPage_WhenTurningItIntoADetailAddressAndBack_ThenTheSelectionIsUnchanged()
    {
        var selection = new SubmissionSelection("kontakt", "todo", 2);
        var url = selection.DetailUrl("s07");
        var query = url[url.IndexOf('?', StringComparison.Ordinal)..];

        // FromQuery(null, ...) is what the detail page does: its route has no slug segment, so the form has
        // to come out of the query. Passing the slug in here instead would leave that leg unexercised.
        Assert.Contains("formular=kontakt", url, StringComparison.Ordinal);
        Assert.Equal(selection, SubmissionSelection.FromQuery(null, query));
    }

    [Theory]
    [InlineData("")]
    [InlineData("?seite=0")]
    [InlineData("?seite=-3")]
    [InlineData("?seite=abc")]
    public void GivenAnAddressWithNoUsablePageNumber_WhenReadingItBack_ThenTheFirstPageApplies(string query) =>
        Assert.Equal(1, SubmissionSelection.FromQuery(null, query).Page);

    [Fact]
    public void GivenAnAddressWithAFilterTheListDoesNotOffer_WhenReadingItBack_ThenNoFilterApplies() =>
        Assert.Null(SubmissionSelection.FromQuery(null, "?filter=erledigt").Quick);

    /// <summary>The whitelist has to let every filter the list offers through - narrowing it silently drops
    /// a bookmarked selection, which looks exactly like the address being read correctly.</summary>
    [Theory]
    [InlineData("new")]
    [InlineData("todo")]
    [InlineData("waiting")]
    [InlineData("failed")]
    public void GivenAnAddressWithAFilterTheListOffers_WhenReadingItBack_ThenThatFilterSurvives(string quick)
    {
        Assert.Contains(quick, SubmissionSelection.QuickFilters);
        Assert.Equal(quick, SubmissionSelection.FromQuery(null, $"?filter={quick}").Quick);
    }

    [Fact]
    public void GivenABareDetailAddressWithoutASelection_WhenReadingItBack_ThenTheDefaultSelectionApplies() =>
        Assert.Equal(SubmissionSelection.Default, SubmissionSelection.FromQuery(null, ""));

    [Fact]
    public void GivenASelection_WhenBuildingTheListAddress_ThenItCarriesFormFilterAndPage()
    {
        var url = new SubmissionSelection("kontakt", "todo", 2).ListUrl();

        Assert.StartsWith("einsendungen/kontakt", url, StringComparison.Ordinal);
        Assert.Equal(new SubmissionSelection("kontakt", "todo", 2),
            SubmissionSelection.FromQuery("kontakt", url[url.IndexOf('?', StringComparison.Ordinal)..]));
    }

    [Fact]
    public void GivenASelection_WhenBuildingTheDetailAddressOfASubmission_ThenItPointsAtTheOwnPageOfThatSubmission() =>
        Assert.StartsWith("einsendung/s07", new SubmissionSelection("kontakt", "todo", 2).DetailUrl("s07"),
            StringComparison.Ordinal);

    [Fact]
    public void GivenASelectionOfEighteenSubmissions_WhenCountingItsPages_ThenFifteenGoOnEachPage() =>
        Assert.Equal(2, SubmissionSelection.PageCount(Kontakt(new SubmissionSelection("kontakt", null, 1))));

    [Fact]
    public void GivenAnEmptySelection_WhenCountingItsPages_ThenThereIsStillOne() =>
        Assert.Equal(1, SubmissionSelection.PageCount(Array.Empty<SubmissionListItem>()));

    [Fact]
    public void GivenAnAddressNamingAPageTheSelectionHasNot_WhenBringingItIntoRange_ThenTheLastPageApplies()
    {
        var selection = new SubmissionSelection("kontakt", null, 99);

        Assert.Equal(2, selection.Clamped(Kontakt(selection)).Page);
    }

    // ---- The way back names where it leads (AC3) ----

    [Fact]
    public void GivenASelectionWithAFormAndAQuickFilter_WhenLabellingTheWayBack_ThenItNamesBoth() =>
        Assert.Equal("Zurück zu Kontakt · Zu bearbeiten",
            new SubmissionSelection("kontakt", "todo", 1).BackLabel(new Ui(), "Kontakt"));

    [Fact]
    public void GivenASelectionWithoutFormOrFilter_WhenLabellingTheWayBack_ThenItNamesAllSubmissions() =>
        Assert.Equal("Zurück zu allen Einsendungen", SubmissionSelection.Default.BackLabel(new Ui(), null));

    /// <summary>AC3: the label must not widen the selection just because the form's display name is not to
    /// hand - the link underneath still leads to that one form.</summary>
    [Fact]
    public void GivenASelectionOnAFormWhoseNameIsUnknown_WhenLabellingTheWayBack_ThenItNamesTheFormItself() =>
        Assert.Equal("Zurück zu kontakt · Zu bearbeiten",
            new SubmissionSelection("kontakt", "todo", 1).BackLabel(new Ui(), null));

    [Fact]
    public void GivenASelectionFilteredToFailures_WhenLabellingTheWayBack_ThenItNamesThatFilter() =>
        Assert.Equal("Zurück zu allen Einsendungen · Fehler",
            new SubmissionSelection(null, "failed", 1).BackLabel(new Ui(), null));

    [Fact]
    public void GivenTheAdminIsSetToEnglish_WhenLabellingTheWayBack_ThenTheLabelIsTranslated()
    {
        var english = new Ui();
        english.Init("en");

        Assert.Equal("Back to Kontakt · To handle",
            new SubmissionSelection("kontakt", "todo", 1).BackLabel(english, "Kontakt"));
    }

    // ---- The selection is a filtered list in list order (AC2, AC4) ----

    [Fact]
    public void GivenSubmissionsInEveryHandlingState_WhenSelectingTheOnesToWorkOn_ThenTheOthersAreLeftOut()
    {
        var selected = Kontakt(new SubmissionSelection(null, "todo", 1));

        Assert.All(selected, s => Assert.Equal("open", s.Handling));
        Assert.Equal(16, selected.Count);                                  // 24 minus 2 done and 6 without handling
        Assert.DoesNotContain(selected, s => s.Id == "s05" || s.Id == "s11");
    }

    [Fact]
    public void GivenSubmissionsWaitingForTheirConfirmation_WhenSelectingThem_ThenTheOthersAreLeftOut()
    {
        var selected = Kontakt(new SubmissionSelection(null, "waiting", 1));

        Assert.All(selected, s => Assert.Equal(1, s.State));
        Assert.Equal(6, selected.Count);
    }

    [Fact]
    public void GivenSubmissionsThatFailed_WhenSelectingThem_ThenTheOthersAreLeftOut()
    {
        var selected = Kontakt(new SubmissionSelection(null, "failed", 1));

        Assert.All(selected, s => Assert.Equal(2, s.State));
        Assert.Equal(6, selected.Count);
    }

    [Fact]
    public void GivenAStoreThatWasNeverVisited_WhenSelectingWhatIsNew_ThenEverythingIsNew() =>
        Assert.Equal(TestData.AdminSubmissionList().Count,
            new SubmissionSelection(null, "new", 1).Select(TestData.AdminSubmissionList(), lastVisit: null).Count);

    [Fact]
    public void GivenSubmissionsAndALastVisit_WhenSelectingTheNewOnes_ThenOnlyThoseAfterTheVisitAreSelected()
    {
        var all = TestData.AdminSubmissionList();
        var lastVisit = all[5].CreatedAt;

        var selected = new SubmissionSelection(null, "new", 1).Select(all, lastVisit);

        Assert.All(selected, s => Assert.True(s.CreatedAt > lastVisit));
        Assert.Equal(5, selected.Count);
    }

    [Fact]
    public void GivenASelectionOnOneForm_WhenWalkingIt_ThenNoSubmissionOfAnotherFormIsReached()
    {
        var selected = Kontakt(new SubmissionSelection("kontakt", null, 1));

        Assert.All(selected, s => Assert.Equal("kontakt", s.Slug));
        Assert.Equal(18, selected.Count);
    }

    [Fact]
    public void GivenASubmissionThatIsNotInTheSelection_WhenAskingAboutIt_ThenItHasNoPlaceAndNoNeighbours()
    {
        // Page 99 so that "stays in range" is distinguishable from "left alone" - the case the fallback exists
        // for is precisely an address carried back from a submission the selection no longer holds.
        var selection = new SubmissionSelection("kontakt", "todo", 99);
        var items = Kontakt(selection);

        Assert.Equal(0, SubmissionSelection.PositionOf(items, "s05"));      // filtered out: handling "done"
        Assert.Null(SubmissionSelection.Next(items, "s05"));
        Assert.Null(SubmissionSelection.Previous(items, "s05"));
        Assert.Equal(2, selection.AtPageContaining(items, "s05").Page);     // 16 open kontakt rows -> 2 pages
    }

    // ---- Walking the neighbours (AC4) ----

    [Fact]
    public void GivenASelectionSpanningTwoPages_WhenAskingForTheNeighbourOfTheLastSubmissionOfPageOne_ThenItIsTheFirstOfPageTwo()
    {
        var selection = new SubmissionSelection("kontakt", null, 1);
        var items = Kontakt(selection);
        var lastOfPageOne = selection.PageSlice(items)[^1];

        Assert.Equal(items[SubmissionSelection.PerPage].Id, SubmissionSelection.Next(items, lastOfPageOne.Id));
    }

    [Fact]
    public void GivenAQuickFilteredSelection_WhenWalkingItFromTheFirstSubmission_ThenEverySubmissionOfTheSelectionIsReachedInListOrder()
    {
        var items = Kontakt(new SubmissionSelection("kontakt", "todo", 1));

        var walked = new List<string> { items[0].Id };
        while (SubmissionSelection.Next(items, walked[^1]) is { } next) walked.Add(next);

        Assert.Equal(items.Select(s => s.Id), walked);
    }

    [Fact]
    public void GivenASelectionSpanningTwoPages_WhenAskingForTheSubmissionBeforeTheFirstOfPageTwo_ThenItIsTheLastOfPageOne()
    {
        var selection = new SubmissionSelection("kontakt", null, 1);
        var items = Kontakt(selection);

        Assert.Equal(selection.PageSlice(items)[^1].Id,
            SubmissionSelection.Previous(items, items[SubmissionSelection.PerPage].Id));
    }

    [Fact]
    public void GivenAQuickFilteredSelection_WhenWalkingItBackFromTheLastSubmission_ThenEverySubmissionOfTheSelectionIsReachedInReverseListOrder()
    {
        var items = Kontakt(new SubmissionSelection("kontakt", "todo", 1));

        var walked = new List<string> { items[^1].Id };
        while (SubmissionSelection.Previous(items, walked[^1]) is { } previous) walked.Add(previous);

        Assert.Equal(items.Reverse().Select(s => s.Id), walked);
    }

    [Fact]
    public void GivenASubmissionInASelection_WhenAskingForItsPosition_ThenItIsCountedAcrossThePages()
    {
        var items = Kontakt(new SubmissionSelection("kontakt", null, 1));

        Assert.Equal(items.Count, SubmissionSelection.PositionOf(items, items[^1].Id));
    }

    // ---- The ends of the selection (AC5) ----

    [Fact]
    public void GivenTheFirstSubmissionOfASelection_WhenAskingForThePreviousOne_ThenThereIsNone()
    {
        var items = Kontakt(new SubmissionSelection("kontakt", null, 1));

        Assert.Null(SubmissionSelection.Previous(items, items[0].Id));
    }

    [Fact]
    public void GivenTheLastSubmissionOfASelection_WhenAskingForTheNextOne_ThenThereIsNone()
    {
        var items = Kontakt(new SubmissionSelection("kontakt", null, 1));

        Assert.Null(SubmissionSelection.Next(items, items[^1].Id));
    }

    [Fact]
    public void GivenASelectionWithASingleSubmission_WhenAskingForBothNeighbours_ThenNeitherExists()
    {
        var only = Kontakt(new SubmissionSelection("kontakt", null, 1)).Take(1).ToList();

        Assert.Null(SubmissionSelection.Previous(only, only[0].Id));
        Assert.Null(SubmissionSelection.Next(only, only[0].Id));
    }

    // ---- The way back lands on the page holding what is shown (AC2) ----

    [Fact]
    public void GivenASubmissionOnTheSecondPageOfASelection_WhenMovingTheSelectionToIt_ThenTheSelectionShowsThatPage()
    {
        var selection = new SubmissionSelection("kontakt", null, 1);
        var items = Kontakt(selection);

        Assert.Equal(2, selection.AtPageContaining(items, items[SubmissionSelection.PerPage].Id).Page);
    }

    [Fact]
    public void GivenASubmissionOnThePageTheReaderCameFrom_WhenMovingTheSelectionToIt_ThenThePageIsUnchanged()
    {
        var selection = new SubmissionSelection("kontakt", null, 2);
        var items = Kontakt(selection);

        Assert.Equal(selection, selection.AtPageContaining(items, selection.PageSlice(items)[0].Id));
    }

    // ---- #13: the personal filters ----

    private static IReadOnlyList<SubmissionListItem> Assigned(SubmissionSelection selection) =>
        selection.Select(TestData.AssignedSubmissionList(), lastVisit: null);

    private static string[] IdsOf(IReadOnlyList<SubmissionListItem> selection) =>
        [.. selection.Select(s => s.Id)];

    /// <summary>
    /// AC4. "Meine offenen" is not a view of one form: whoever works through their own requests wants all
    /// of them, and the list already holds every form. What it is not is everything with my name on it -
    /// an assignee filter means open and assigned, here and in AC5 alike.
    /// </summary>
    [Fact]
    public void GivenSubmissionsOfSeveralForms_WhenFilteringByTheSignedInAdmin_ThenTheirOpenOnesOfEveryFormAppear()
    {
        var mine = Assigned(new SubmissionSelection(null, null, 1, TestData.AdminMe));

        Assert.Equal(new[] { "a1", "a2" }, IdsOf(mine));
        Assert.Equal(new[] { "kontakt", "whitepaper" }, mine.Select(s => s.Slug).ToArray());
    }

    /// <summary>AC5: the same rule, someone else's name.</summary>
    [Fact]
    public void GivenSubmissionsOfSeveralForms_WhenFilteringByAnotherAdmin_ThenOnlyThatAdminsOpenOnesAppear()
    {
        var theirs = Assigned(new SubmissionSelection(null, null, 1, TestData.AdminColleague));

        Assert.Equal(new[] { "a3", "a4" }, IdsOf(theirs));
    }

    /// <summary>
    /// The "open" half of AC4 and AC5, on its own: a submission the assignee has already finished is not
    /// what a personal filter is for. Without this the filter would grow into an archive of everything
    /// that person ever touched.
    /// </summary>
    [Fact]
    public void GivenAnAssigneeFilter_WhenOneOfTheirSubmissionsIsAlreadyDone_ThenItIsNotInTheSelection() =>
        Assert.DoesNotContain("a6", IdsOf(Assigned(new SubmissionSelection(null, null, 1, TestData.AdminMe))));

    /// <summary>
    /// AC6, second half: after the assignment is gone the submission is in nobody's personal filter. It is
    /// still open and still in the list - it has just stopped being anyone's.
    /// </summary>
    [Fact]
    public void GivenASubmissionWithoutAnAssignee_WhenAnyPersonalFilterIsApplied_ThenItIsInNoneOfThem()
    {
        Assert.DoesNotContain("a5", IdsOf(Assigned(new SubmissionSelection(null, null, 1, TestData.AdminMe))));
        Assert.DoesNotContain("a5", IdsOf(Assigned(new SubmissionSelection(null, null, 1, TestData.AdminColleague))));
    }

    /// <summary>
    /// The picker's third entry: what is open and nobody has taken. It follows the same rule as a name -
    /// a submission of a form without handling is not open, so it is not waiting for anyone either.
    /// </summary>
    [Fact]
    public void GivenTheNobodyFilter_WhenSelecting_ThenOnlyOpenSubmissionsWithoutAnAssigneeAppear() =>
        Assert.Equal(new[] { "a5" }, IdsOf(Assigned(new SubmissionSelection(null, null, 1, SubmissionSelection.Nobody))));

    /// <summary>
    /// A personal filter is a selection like any other, so it has to survive the trip to a submission and
    /// back - otherwise the way back out of the detail view lands in a wider list than the reader left.
    /// </summary>
    [Fact]
    public void GivenAnAssigneeFilterInTheAddress_WhenTurningItIntoADetailAddressAndBack_ThenTheSelectionIsUnchanged()
    {
        var selection = new SubmissionSelection("kontakt", null, 2, TestData.AdminMe);
        var url = selection.DetailUrl("a1");

        Assert.Equal(selection, SubmissionSelection.FromQuery(null, url[url.IndexOf('?', StringComparison.Ordinal)..]));
    }

    /// <summary>The back link has to name the narrowing it returns to, or it promises a list it does not lead to.</summary>
    [Fact]
    public void GivenAnAssigneeFilter_WhenNamingTheWayBack_ThenTheLabelSaysWhoseSubmissionsTheseAre() =>
        Assert.Contains(TestData.AdminMe, new SubmissionSelection(null, null, 1, TestData.AdminMe).BackLabel(new Ui(), null),
            StringComparison.Ordinal);

    /// <summary>The mirror of the one above - the unassigned filter is a narrowing too, and drops out of the
    /// label just as silently when nothing pins it.</summary>
    [Fact]
    public void GivenTheNobodyFilter_WhenNamingTheWayBack_ThenTheLabelSaysTheseAreUnassigned() =>
        Assert.Contains("ohne Bearbeiter", new SubmissionSelection(null, null, 1, SubmissionSelection.Nobody).BackLabel(new Ui(), null),
            StringComparison.Ordinal);

    // ---- #13: "Meine offenen" as an operation, not as markup ----

    /// <summary>
    /// AC4's "formularübergreifend" is a property of the chip, not only of the filter behind it. The list is
    /// reachable per form, so the state this widens out of is one a reader is actually in - and narrowing
    /// further from there would answer "my open ones" with one form's worth of them.
    /// </summary>
    [Fact]
    public void GivenAFormAndAQuickFilter_WhenChoosingThePersonalFilter_ThenItWidensToEveryFormAndPageOne()
    {
        var chosen = new SubmissionSelection("kontakt", "failed", 3).TogglePersonal(TestData.AdminMe);

        Assert.Equal(new SubmissionSelection(null, null, 1, TestData.AdminMe), chosen);
    }

    /// <summary>Choosing it again gives the narrowing back, and nothing else with it.</summary>
    [Fact]
    public void GivenThePersonalFilterIsOn_WhenChoosingItAgain_ThenOnlyThatNarrowingIsGone()
    {
        var selection = new SubmissionSelection("kontakt", "todo", 2, TestData.AdminMe);

        Assert.Equal(new SubmissionSelection("kontakt", "todo", 1), selection.TogglePersonal(TestData.AdminMe));
    }

    /// <summary>
    /// The chip is lit exactly while the list it stands for is the list on screen. Paging through one's
    /// own open ones stays inside that filter, so the page must not put the light out.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public void GivenThePersonalFilterOnAnyPage_WhenTheChipAsksWhetherItIsLit_ThenItIs(int page) =>
        Assert.True(new SubmissionSelection(null, null, page, TestData.AdminMe).IsPersonal(TestData.AdminMe));

    /// <summary>
    /// The mirror, and the reason this is not simply "is the assignee me": both the form select and the
    /// quick filters deliberately keep the assignee, so a selection can carry my name and still not be
    /// the cross-form set the badge counts. A chip lit there would claim a list that is not on screen.
    /// </summary>
    [Fact]
    public void GivenAPersonalFilterNarrowedFurther_WhenTheChipAsksWhetherItIsLit_ThenItIsNot()
    {
        Assert.False(new SubmissionSelection("kontakt", null, 1, TestData.AdminMe).IsPersonal(TestData.AdminMe));
        Assert.False(new SubmissionSelection(null, "failed", 1, TestData.AdminMe).IsPersonal(TestData.AdminMe));
    }

    [Fact]
    public void GivenAnotherAdminsFilter_WhenTheChipAsksWhetherItIsLit_ThenItIsNot() =>
        Assert.False(SubmissionSelection.Personal(TestData.AdminColleague).IsPersonal(TestData.AdminMe));


}
