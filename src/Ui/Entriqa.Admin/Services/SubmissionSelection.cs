using System.Globalization;
using System.Web;

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

    /// <summary>
    /// Reads a selection back out of a route slug and a query string ("?filter=todo&amp;seite=2").
    /// The list passes the slug of its route, the detail page passes null and lets "formular" supply it -
    /// the detail route has no slug segment to read. A page that is missing, unparsable or below one falls
    /// back to the first: an address is something people edit and share, and it must survive that (AC6).
    /// </summary>
    public static SubmissionSelection FromQuery(string? slug, string query)
    {
        var q = HttpUtility.ParseQueryString(query);
        var page = int.TryParse(q["seite"], NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) && n > 1 ? n : 1;
        return new SubmissionSelection(slug ?? Set(q["formular"]), Set(q["filter"]), page);
    }

    /// <summary>The query part of an address; empty when nothing but the defaults is set.</summary>
    public string ToQuery() => Query(withForm: false);

    /// <summary>The list address this selection returns to - the form stays the route segment it is today.</summary>
    public string ListUrl() =>
        "einsendungen" + (Slug is null ? "" : "/" + Uri.EscapeDataString(Slug)) + ToQuery();

    /// <summary>
    /// The detail address of one submission, carrying this selection with it. The form travels in the query
    /// here because the detail route addresses the submission, not the form.
    /// </summary>
    public string DetailUrl(string id) => "einsendung/" + Uri.EscapeDataString(id) + Query(withForm: true);

    private string Query(bool withForm)
    {
        var parts = new List<string>(3);
        if (withForm && Slug is not null) parts.Add("formular=" + Uri.EscapeDataString(Slug));
        if (Quick is not null) parts.Add("filter=" + Uri.EscapeDataString(Quick));
        if (Page > 1) parts.Add("seite=" + Page.ToString(CultureInfo.InvariantCulture));
        return parts.Count == 0 ? "" : "?" + string.Join("&", parts);
    }

    private static string? Set(string? value) => string.IsNullOrEmpty(value) ? null : value;

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
