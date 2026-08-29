namespace Entriqa.Admin.Services;

/// <summary>
/// The selection a reader is looking at: which form, which quick filter, which page. Both the list
/// and the detail page derive everything from this one value - the list to render itself, the detail
/// page to name its way back and to walk the neighbours. It therefore lives in the URL rather than in
/// a field, which is what makes the way back (AC2) and a linkable address (AC6) possible at all.
///
/// Deliberately free of Blazor types: this is where the story's logic sits, and it is executed by
/// the unit tests rather than scanned as source.
/// </summary>
public sealed record SubmissionSelection(string? Slug, string? Quick, int Page)
{
    public const int PerPage = 15;

    /// <summary>All forms, no quick filter, page one - what a bare address means (AC3, AC4).</summary>
    public static SubmissionSelection Default => new(null, null, 1);

    /// <summary>Reads a selection back out of a route slug and a query string ("?filter=todo&amp;seite=2").</summary>
    public static SubmissionSelection FromQuery(string? slug, string query) => throw new NotImplementedException();

    /// <summary>The query part of an address, empty when nothing but the defaults is set.</summary>
    public string ToQuery() => throw new NotImplementedException();

    /// <summary>The list address this selection returns to.</summary>
    public string ListUrl() => throw new NotImplementedException();

    /// <summary>The detail address of one submission, carrying this selection with it.</summary>
    public string DetailUrl(string id) => throw new NotImplementedException();

    /// <summary>Names the selection the back link returns to (AC3).</summary>
    public string BackLabel(Ui t, string? formName) => throw new NotImplementedException();

    /// <summary>The submissions of this selection, in the order the list shows them.</summary>
    public IReadOnlyList<SubmissionListItem> Select(
        IReadOnlyList<SubmissionListItem> all, DateTimeOffset? lastVisit) => throw new NotImplementedException();

    /// <summary>The slice of the selection this page shows.</summary>
    public IReadOnlyList<SubmissionListItem> PageSlice(
        IReadOnlyList<SubmissionListItem> selection) => throw new NotImplementedException();

    public int PageCount(IReadOnlyList<SubmissionListItem> selection) => throw new NotImplementedException();

    /// <summary>The same selection, moved to the page that holds this submission - where the back link goes.</summary>
    public SubmissionSelection AtPageContaining(
        IReadOnlyList<SubmissionListItem> selection, string id) => throw new NotImplementedException();

    /// <summary>The next submission of the selection, or null at its end (AC4, AC5).</summary>
    public static string? Next(IReadOnlyList<SubmissionListItem> selection, string id) => throw new NotImplementedException();

    /// <summary>The previous submission of the selection, or null at its start (AC4, AC5).</summary>
    public static string? Previous(IReadOnlyList<SubmissionListItem> selection, string id) => throw new NotImplementedException();

    /// <summary>One-based position within the selection, for the "n von m" counter; 0 when it is not in it.</summary>
    public static int PositionOf(IReadOnlyList<SubmissionListItem> selection, string id) => throw new NotImplementedException();
}
