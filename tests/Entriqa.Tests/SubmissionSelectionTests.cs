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
        selection.Select(TestData.SubmissionList(), lastVisit: null);

    // ---- The address carries the selection (AC2, AC6) ----

    [Fact]
    public void GivenASelectionWithFormFilterAndPage_WhenTurningItIntoADetailAddressAndBack_ThenTheSelectionIsUnchanged()
    {
        var selection = new SubmissionSelection("kontakt", "todo", 2);
        var url = selection.DetailUrl("s07");
        var query = url[url.IndexOf('?', StringComparison.Ordinal)..];

        Assert.Equal(selection, SubmissionSelection.FromQuery("kontakt", query));
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

    // ---- The way back names where it leads (AC3) ----

    [Fact]
    public void GivenASelectionWithAFormAndAQuickFilter_WhenLabellingTheWayBack_ThenItNamesBoth() =>
        Assert.Equal("Zurück zu Kontakt · Zu bearbeiten",
            new SubmissionSelection("kontakt", "todo", 1).BackLabel(new Ui(), "Kontakt"));

    [Fact]
    public void GivenASelectionWithoutFormOrFilter_WhenLabellingTheWayBack_ThenItNamesAllSubmissions() =>
        Assert.Equal("Zurück zu allen Einsendungen", SubmissionSelection.Default.BackLabel(new Ui(), null));

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
    public void GivenSubmissionsInEveryHandlingState_WhenSelectingTheOnesToWorkOn_ThenOnlyOpenOnesAreSelected() =>
        Assert.All(Kontakt(new SubmissionSelection(null, "todo", 1)), s => Assert.Equal("open", s.Handling));

    [Fact]
    public void GivenSubmissionsAndALastVisit_WhenSelectingTheNewOnes_ThenOnlyThoseAfterTheVisitAreSelected()
    {
        var all = TestData.SubmissionList();
        var lastVisit = all[5].CreatedAt;

        var selected = new SubmissionSelection(null, "new", 1).Select(all, lastVisit);

        Assert.All(selected, s => Assert.True(s.CreatedAt > lastVisit));
        Assert.Equal(5, selected.Count);
    }

    [Fact]
    public void GivenASelectionOnOneForm_WhenWalkingIt_ThenNoSubmissionOfAnotherFormIsReached() =>
        Assert.All(Kontakt(new SubmissionSelection("kontakt", null, 1)), s => Assert.Equal("kontakt", s.Slug));

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
}
