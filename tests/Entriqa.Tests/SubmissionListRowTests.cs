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
    [Fact]
    public void GivenTheSubmissionsList_WhenInspectingItsRows_ThenTheyTakeFocusAndAnswerTheKeyboard()
    {
        var markup = File.ReadAllText(Path.Combine(AdminDirectory(), "Pages", "Submissions.razor"));
        var row = RowTag(markup);
        Assert.NotNull(row);

        Assert.Contains("tabindex=\"0\"", row, StringComparison.Ordinal);
        Assert.Contains("@onkeydown", row, StringComparison.Ordinal);
    }

    /// <summary>
    /// The opening tag of the clickable row, with its attributes. The closing &gt; is searched for outside
    /// quotes, exactly as EditorChangedBindingTests does: an event handler in the tag is a lambda, so a
    /// naive scan to the first &gt; stops inside <c>() =&gt; Open(…)</c> and silently drops every attribute
    /// written after it - which would let this guard pass while reading half a tag.
    /// </summary>
    private static string? RowTag(string markup)
    {
        var start = markup.IndexOf("<tr class=\"rowlink", StringComparison.Ordinal);
        Assert.True(start >= 0, "Submissions.razor no longer has a row carrying the rowlink class - update this guard.");

        var quoted = false;
        for (var i = start; i < markup.Length; i++)
        {
            if (markup[i] == '"') quoted = !quoted;
            else if (markup[i] == '>' && !quoted) return markup[start..(i + 1)];
        }
        return null;
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
