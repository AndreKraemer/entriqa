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
        Assert.Contains("<a href=\"@Shown.DetailUrl(item.Id)\"", RowBlock(), StringComparison.Ordinal);

    /// <summary>
    /// The cell holding the link keeps the click to itself. Otherwise a ctrl/cmd/shift-click opens the new
    /// tab the browser is asked for *and* navigates the current one through the row's own handler - which
    /// would quietly take back the "opens in a new tab" that is half the reason for the anchor.
    /// </summary>
    [Fact]
    public void GivenTheSubmissionsList_WhenTheLinkIsClicked_ThenTheRowDoesNotNavigateAsWell() =>
        Assert.Contains("<td @onclick:stopPropagation=\"true\"><a href=", RowBlock(), StringComparison.Ordinal);

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

    private static string Markup() => AdminMarkup.Read("Pages", "Submissions.razor");

    /// <summary>Everything from the opening row tag to its &lt;/tr&gt; - the cells included.</summary>
    private static string RowBlock() => AdminMarkup.Between(Markup(), "<tr class=\"rowlink", "</tr>",
        "Submissions.razor no longer has a closed row carrying the rowlink class - update this guard.");

    private static string RowTag()
    {
        var markup = Markup();
        return AdminMarkup.Tags(markup[RowStart(markup)..], "<tr").First();
    }

    private static int RowStart(string markup)
    {
        var start = markup.IndexOf("<tr class=\"rowlink", StringComparison.Ordinal);
        Assert.True(start >= 0, "Submissions.razor no longer has a row carrying the rowlink class - update this guard.");
        return start;
    }
}
