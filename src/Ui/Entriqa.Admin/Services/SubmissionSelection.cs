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

    /// <summary>
    /// The submissions of this selection, in the order the list shows them. The incoming order is kept
    /// rather than re-sorted: it is the order the endpoint returns and therefore the order the reader saw,
    /// which is what the arrows have to follow (AC4). The form is filtered here as well, although the
    /// endpoint already narrows by slug - so a caller that loaded every form can still ask for one.
    /// </summary>
    public IReadOnlyList<SubmissionListItem> Select(IReadOnlyList<SubmissionListItem> all, DateTimeOffset? lastVisit) =>
        all.Where(s => Slug is null || s.Slug == Slug)
           .Where(s => Quick switch
           {
               "new" => lastVisit is null || s.CreatedAt > lastVisit,
               "todo" => s.Handling == "open",
               "waiting" => s.State == 1,
               "failed" => s.State == 2,
               _ => true,
           })
           .ToList();

    /// <summary>The slice of the selection this page shows.</summary>
    public IReadOnlyList<SubmissionListItem> PageSlice(IReadOnlyList<SubmissionListItem> selection) =>
        selection.Skip((Page - 1) * PerPage).Take(PerPage).ToList();

    /// <summary>How many pages the selection has; always at least one, so an empty selection still has a page.</summary>
    public static int PageCount(IReadOnlyList<SubmissionListItem> selection) =>
        Math.Max(1, (selection.Count + PerPage - 1) / PerPage);

    /// <summary>
    /// The same selection, moved to the page that holds this submission - where the back link goes. Walking
    /// the selection in the detail view can leave the page the reader came from, and then "the page before"
    /// no longer exists as an answer; the page showing what is on screen is the one that does (AC2, AC4).
    /// </summary>
    public SubmissionSelection AtPageContaining(IReadOnlyList<SubmissionListItem> selection, string id)
    {
        var index = IndexOf(selection, id);
        return index < 0 ? this : this with { Page = index / PerPage + 1 };
    }

    private static int IndexOf(IReadOnlyList<SubmissionListItem> selection, string id)
    {
        for (var i = 0; i < selection.Count; i++) if (selection[i].Id == id) return i;
        return -1;
    }

    /// <summary>
    /// The next submission of the selection, or null at its end (AC4, AC5). Neighbours span the whole
    /// selection, not the page it happens to sit on - a reader working through twenty open requests should
    /// not stop at fifteen. A submission the selection does not contain has no neighbours in either
    /// direction, which is what a stale address degrades to.
    /// </summary>
    public static string? Next(IReadOnlyList<SubmissionListItem> selection, string id)
    {
        var index = IndexOf(selection, id);
        return index >= 0 && index + 1 < selection.Count ? selection[index + 1].Id : null;
    }

    /// <summary>The previous submission of the selection, or null at its start (AC4, AC5).</summary>
    public static string? Previous(IReadOnlyList<SubmissionListItem> selection, string id)
    {
        var index = IndexOf(selection, id);
        return index > 0 ? selection[index - 1].Id : null;
    }

    /// <summary>One-based position within the selection, for the "n von m" counter; 0 when it is not in it.</summary>
    public static int PositionOf(IReadOnlyList<SubmissionListItem> selection, string id) => IndexOf(selection, id) + 1;
}
