using Xunit;

namespace Entriqa.Tests;

/// <summary>
/// Issue #11, AC1: a row of the submissions list opens the submission - by mouse and by keyboard. Neither
/// half has a runtime surface. The keyboard half is a real anchor rather than a key handler on the row: a
/// &lt;tr&gt; cannot carry role="link" without dropping the row and its cells out of the table for assistive
/// technology, while an anchor takes focus, answers Enter and opens in a new tab for free. So what has to
/// be guarded is that the anchor is there and points at the detail address, and that the row still reacts
/// to a click. The markup is where both live, so the markup is what this reads (precedent:
/// EditorChangedBindingTests).
/// </summary>
public class SubmissionListRowTests
{
    [Fact]
    public void GivenTheSubmissionsList_WhenInspectingItsRows_ThenAClickOpensTheSubmission() =>
        Assert.Contains("@onclick", RowTag(), StringComparison.Ordinal);

    [Fact]
    public void GivenTheSubmissionsList_WhenInspectingItsRows_ThenTheyCarryALinkToTheDetailAddress() =>
        Assert.Contains("<a href=\"@Shown.DetailUrl(", RowBlock(), StringComparison.Ordinal);

    /// <summary>
    /// The row must not present itself as anything but a row. role="link" on a &lt;tr&gt; is invalid
    /// ARIA-in-HTML and costs a screen reader the cells; this pins the decision so it is not reintroduced.
    /// </summary>
    [Fact]
    public void GivenTheSubmissionsList_WhenInspectingItsRows_ThenTheyDoNotOverrideTheTableSemantics()
    {
        Assert.DoesNotContain("role=", RowTag(), StringComparison.Ordinal);
        Assert.DoesNotContain("tabindex=", RowTag(), StringComparison.Ordinal);
    }

    private static string Markup() =>
        File.ReadAllText(Path.Combine(AdminDirectory(), "Pages", "Submissions.razor"));

    /// <summary>Everything from the opening row tag to its &lt;/tr&gt; - the cells included.</summary>
    private static string RowBlock()
    {
        var markup = Markup();
        var start = RowStart(markup);
        var end = markup.IndexOf("</tr>", start, StringComparison.Ordinal);
        Assert.True(end > start, "the clickable row is not closed - update this guard.");
        return markup[start..end];
    }

    /// <summary>
    /// The opening tag of the clickable row with its attributes. The closing &gt; is searched for outside
    /// quotes, exactly as EditorChangedBindingTests does: an event handler in the tag is a lambda, so a
    /// naive scan to the first &gt; stops inside <c>() =&gt; Open(…)</c> and silently drops every attribute
    /// written after it - which would let a guard pass while reading half a tag.
    /// </summary>
    private static string RowTag()
    {
        var markup = Markup();
        var start = RowStart(markup);
        var quoted = false;
        for (var i = start; i < markup.Length; i++)
        {
            if (markup[i] == '"') quoted = !quoted;
            else if (markup[i] == '>' && !quoted) return markup[start..(i + 1)];
        }
        throw new InvalidOperationException("the clickable row's opening tag is not closed - update this guard.");
    }

    private static int RowStart(string markup)
    {
        var start = markup.IndexOf("<tr class=\"rowlink", StringComparison.Ordinal);
        Assert.True(start >= 0, "Submissions.razor no longer has a row carrying the rowlink class - update this guard.");
        return start;
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
