using Xunit;

namespace Entriqa.Tests;

/// <summary>
/// Issue #11: an address can name a page the selection has not got - hand-edited, shared after the filter
/// moved on, or carried back from a submission no longer in it. SubmissionSelectionTests proves that
/// <see cref="Entriqa.Admin.Services.SubmissionSelection.Clamped"/> brings such a page into range; that the
/// list actually renders through it is markup. Without this guard, dropping the call leaves the whole suite
/// green while "Seite 99 von 2" returns over an empty table.
/// </summary>
public class SubmissionListPagingTests
{
    [Fact]
    public void GivenTheSubmissionsList_WhenItRendersAPage_ThenItIsOneTheSelectionCanShow()
    {
        var markup = AdminMarkup.Read("Pages", "Submissions.razor");

        Assert.Contains("Shown => _selection.Clamped(Filtered)", markup, StringComparison.Ordinal);
        Assert.Contains("Paged => Shown.PageSlice(Filtered)", markup, StringComparison.Ordinal);
    }
}
