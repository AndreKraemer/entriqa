using Xunit;

namespace Entriqa.Tests;

/// <summary>
/// Issue #11: form, quick filter and page live in the address. Blazor's router re-renders a page when the
/// *path* changes, but not when only the query does - and here the query is where the quick filter and the
/// page number live, so every filter button and every page turn is a query-only navigation. Without an
/// explicit subscription the address moves and the page does not: the list keeps rendering the previous
/// selection and its row links keep pointing at it. The acceptance gate found exactly that, with the whole
/// unit suite green, because no test host renders these components.
///
/// So this reads the source. It cannot prove the page reacts - only driving the app does that - but it
/// fails the moment the mechanism is removed, which is the regression worth catching.
/// </summary>
public class SubmissionAddressReactionTests
{
    [Theory]
    [InlineData("Submissions.razor")]
    [InlineData("SubmissionDetailPage.razor")]
    public void GivenAPageWhoseSelectionLivesInTheAddress_WhenInspectingIt_ThenItSubscribesToLocationChanges(string page)
    {
        var markup = AdminMarkup.Read("Pages", page);

        Assert.Contains("Nav.LocationChanged += OnLocationChanged", markup, StringComparison.Ordinal);
    }

    /// <summary>A subscription on a component that is navigated away from leaks it; the pages are disposable
    /// for that reason and for no other, so the two belong in one guard.</summary>
    [Theory]
    [InlineData("Submissions.razor")]
    [InlineData("SubmissionDetailPage.razor")]
    public void GivenAPageThatSubscribesToLocationChanges_WhenInspectingIt_ThenItUnsubscribesWhenDisposed(string page)
    {
        var markup = AdminMarkup.Read("Pages", page);

        Assert.Contains("@implements IDisposable", markup, StringComparison.Ordinal);
        Assert.Contains("Nav.LocationChanged -= OnLocationChanged", markup, StringComparison.Ordinal);
    }
}
