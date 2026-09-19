using Xunit;

namespace Entriqa.Tests;

/// <summary>
/// Issue #16, the page side of the story (criteria 1, 4, 6). Nothing renders <c>Stats.razor</c> in this
/// test host, so - as with <see cref="SubmissionAddressReactionTests"/> - this reads the source. It cannot
/// prove the overview renders (only <c>/pairmode:acceptance</c> does that), but it fails the moment the
/// no-form branch stops being wired to the cross-form endpoint or falls back to the old placeholder, which
/// is the regression worth catching. Criterion 3 (no quiz metrics) needs no guard here: <c>OverallStats</c>
/// carries no quiz member, so the overview markup cannot reference one - the compiler enforces it.
/// </summary>
public class OverallStatsPageGuardTests
{
    private static string Stats() => AdminMarkup.Read("Pages", "Stats.razor");

    [Fact]
    public void GivenNoFormIsChosen_WhenInspectingTheStatsPage_ThenItLoadsTheCrossFormOverview()
    {
        var markup = Stats();

        // The no-form case routes to the overview endpoint, the chosen-form case keeps the per-form one (criterion 4).
        Assert.Contains("Slug is null", markup, StringComparison.Ordinal);
        Assert.Contains("Api.GetOverallStatsAsync()", markup, StringComparison.Ordinal);
        Assert.Contains("Api.GetStatsAsync(", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void GivenTheStatsPage_WhenInspectingTheNoFormBranch_ThenTheOldPlaceholderNoLongerStandsInForIt()
    {
        // Before #16 the no-form state showed only this line; the overview replaces it.
        Assert.DoesNotContain("Wähle ein veröffentlichtes Formular.", Stats(), StringComparison.Ordinal);
    }

    [Fact]
    public void GivenNoPublishedForm_WhenInspectingTheStatsPage_ThenItExplainsInsteadOfShowingEmptyTiles()
    {
        var markup = Stats();

        Assert.Contains("_overall.Forms.Count == 0", markup, StringComparison.Ordinal);
        Assert.Contains("Noch kein Formular veröffentlicht", markup, StringComparison.Ordinal);
    }
}
