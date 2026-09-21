using System.Text.RegularExpressions;
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

        // Read the wiring, not just the names: the overview call must sit *inside the `Slug is null`
        // branch*, so that swapping the branch condition (Slug is not null) - a miswiring the two calls
        // being present anywhere would not reveal - fails this test. See test-conventions: "the pull is
        // what makes it a guard rather than a grep".
        Assert.Matches(
            new Regex(@"Slug\s+is\s+null\s*\)\s*\{\s*_overall\s*=\s*await\s+Api\.GetOverallStatsAsync\(", RegexOptions.Singleline),
            markup);
        // Criterion 4: the chosen-form path keeps the existing per-form call.
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

    [Fact]
    public void GivenAFormWithoutFunnelStarts_WhenInspectingTheStatsPage_ThenItsCompletionRateIsGuardedNotDividedByOne()
    {
        var markup = Stats();

        // The completion cell must guard the zero-denominator case rather than divide by Max(Starts,1),
        // which renders an impossible >100% rate (acceptance FAIL for #16). Reads the guard, not just a name.
        Assert.Matches(new Regex(@"f\.Starts\s*==\s*0\s*\?", RegexOptions.Singleline), markup);
        Assert.DoesNotContain("Math.Max(f.Starts", markup, StringComparison.Ordinal);
    }
}
