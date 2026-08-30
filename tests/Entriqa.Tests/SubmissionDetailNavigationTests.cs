using Xunit;

namespace Entriqa.Tests;

/// <summary>
/// Issue #11, AC5: at the first and the last submission of a selection the respective direction must not be
/// triggerable. SubmissionSelectionTests proves that Next and Previous have no answer there; that the arrows
/// are actually bound to it lives in the markup and has no runtime surface, so removing either binding leaves
/// the whole suite green while both ends become clickable. This reads the two tags.
/// </summary>
public class SubmissionDetailNavigationTests
{
    [Theory]
    [InlineData("Previous")]
    [InlineData("Next")]
    public void GivenTheDetailPage_WhenInspectingItsArrows_ThenEachIsDisabledWhereItsDirectionEnds(string direction)
    {
        var markup = File.ReadAllText(Path.Combine(AdminDirectory(), "Pages", "SubmissionDetailPage.razor"));

        Assert.Contains($"disabled=\"@({direction} is null)\"", markup, StringComparison.Ordinal);
        Assert.Contains($"@onclick=\"() => Open({direction})\"", markup, StringComparison.Ordinal);
    }

    private static string AdminDirectory()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var admin = Path.Combine(dir.FullName, "src", "Ui", "Entriqa.Admin");
            if (Directory.Exists(admin)) return admin;
        }
        throw new DirectoryNotFoundException($"Entriqa.Admin not found above {AppContext.BaseDirectory}");
    }
}
