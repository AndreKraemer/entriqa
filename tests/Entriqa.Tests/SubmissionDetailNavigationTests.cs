using Xunit;

namespace Entriqa.Tests;

/// <summary>
/// Issue #11, AC5: at the first and the last submission of a selection the respective direction must not be
/// triggerable. SubmissionSelectionTests proves that Next and Previous have no answer there; that each arrow
/// is bound to its own direction lives in the markup and has no runtime surface. Both halves are asserted on
/// the same tag - apart, they are satisfied by a page whose two arrows walk the wrong way round.
/// </summary>
public class SubmissionDetailNavigationTests
{
    [Theory]
    [InlineData("Previous")]
    [InlineData("Next")]
    public void GivenTheDetailPage_WhenInspectingItsArrows_ThenEachIsDisabledWhereItsOwnDirectionEnds(string direction)
    {
        var markup = AdminMarkup.Read("Pages", "SubmissionDetailPage.razor");

        var button = Assert.Single(AdminMarkup.Tags(markup, "<button"),
            tag => tag.Contains($"Open({direction})", StringComparison.Ordinal));

        Assert.Contains($"disabled=\"@({direction} is null)\"", button, StringComparison.Ordinal);
    }
}
