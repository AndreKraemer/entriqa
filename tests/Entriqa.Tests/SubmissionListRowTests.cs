using System.Text.RegularExpressions;
using Xunit;

namespace Entriqa.Tests;

/// <summary>
/// Issue #11, AC1: a row of the submissions list opens the submission - on click and on keyboard
/// confirmation. A bare &lt;tr&gt; with an @onclick is reachable by mouse only; it takes no focus and
/// answers no key, so keyboard operation is invisible in every runtime assertion one could write about
/// the list. The markup is what carries it, so the markup is what this reads (the precedent is
/// EditorChangedBindingTests).
/// </summary>
public class SubmissionListRowTests
{
    private static readonly Regex Row = new("""<tr[^>]*class="[^"]*rowlink[^"]*"[^>]*>""", RegexOptions.Compiled);

    [Fact]
    public void GivenTheSubmissionsList_WhenInspectingItsRows_ThenTheyTakeFocusAndAnswerTheKeyboard()
    {
        var markup = File.ReadAllText(Path.Combine(AdminDirectory(), "Pages", "Submissions.razor"));
        var row = Row.Match(markup);
        Assert.True(row.Success, "Submissions.razor no longer has a row carrying the rowlink class - update this guard.");

        Assert.Contains("tabindex=\"0\"", row.Value, StringComparison.Ordinal);
        Assert.Contains("@onkeydown", row.Value, StringComparison.Ordinal);
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
