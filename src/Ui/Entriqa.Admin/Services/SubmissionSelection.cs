using System.Globalization;
using System.Web;

namespace Entriqa.Admin.Services;

/// <summary>
/// The selection a reader is looking at: which form, which quick filter, whose submissions, which
/// page. Both the list
/// and the detail page derive everything from this one value - the list to render itself, the detail
/// page to name its way back and to walk the neighbours. It therefore lives in the URL rather than in
/// a field, which is what makes the way back (AC2) and a linkable address (AC6) possible at all.
///
/// Deliberately free of Blazor types: this is where the story's logic sits, and it is executed by
/// the unit tests rather than scanned as source.
/// </summary>
public sealed record SubmissionSelection(string? Slug, string? Quick, int Page, string? Assignee = null)
{
    public const int PerPage = 15;

    /// <summary>
    /// The assignee filter that means "nobody has taken this" (#13). A single dash can never be an
    /// admin's display name, so it needs no escaping and no second query parameter.
    /// </summary>
    public const string Nobody = "-";

    /// <summary>The quick filters the list offers. An address naming anything else has no filter at all.</summary>
    public static readonly IReadOnlySet<string> QuickFilters =
        new HashSet<string>(StringComparer.Ordinal) { "new", "todo", "waiting", "failed" };

    /// <summary>All forms, no quick filter, page one - what a bare address means (AC3, AC4).</summary>
    public static SubmissionSelection Default => new(null, null, 1);

    /// <summary>
    /// What "Meine offenen" stands for (#13, AC4): every open submission assigned to that admin, across
    /// all forms. It drops the form and the quick filter rather than narrowing further - the list is
    /// reachable per form (Forms.razor links straight to einsendungen/{slug}), and asking for one's own
    /// open ones from there means all of them, not that form's.
    /// </summary>
    public static SubmissionSelection Personal(string who) => new(null, null, 1, who);

    /// <summary>
    /// Choosing "Meine offenen", and choosing it again: into the personal filter, or back out of it while
    /// the rest of the selection stays. One expression for the chip's target and for its badge, so the
    /// number on the button cannot disagree with the list the button leads to.
    /// </summary>
    public SubmissionSelection TogglePersonal(string who) =>
        Assignee == who ? this with { Assignee = null, Page = 1 } : Personal(who);

    /// <summary>
    /// Whether this selection is that admin's personal filter, on whichever page. The chip highlights on
    /// this rather than on the assignee alone: a selection narrowed further - to one form, or by a quick
    /// filter, both of which deliberately keep the assignee - is no longer the cross-form set the badge
    /// counts, and a chip that stayed lit would claim that it was.
    /// </summary>
    public bool IsPersonal(string who) => this with { Page = 1 } == Personal(who);

    /// <summary>
    /// Reads a selection back out of a route slug and a query string ("?filter=todo&amp;seite=2").
    /// The list passes the slug of its route, the detail page passes null and lets "formular" supply it -
    /// the detail route has no slug segment to read. An address is something people edit and share, so it
    /// must survive that (AC6): a page that is missing, unparsable or below one falls back to the first,
    /// and a filter the list does not offer is no filter - otherwise the address would claim a narrowing
    /// that neither the list nor the back label applies.
    /// </summary>
    public static SubmissionSelection FromQuery(string? slug, string query)
    {
        var q = HttpUtility.ParseQueryString(query);
        var page = int.TryParse(q["seite"], NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) && n > 1 ? n : 1;
        var quick = Set(q["filter"]) is { } f && QuickFilters.Contains(f) ? f : null;
        // The assignee is not validated against the admin list on purpose: a name the list no longer
        // holds yields an empty selection, which is honest, where silently dropping the narrowing would
        // show a wider list than the address promises.
        return new SubmissionSelection(slug ?? Set(q["formular"]), quick, page, Set(q["bearbeiter"]));
    }

    /// <summary>The list address this selection returns to - the form stays the route segment it is today.</summary>
    public string ListUrl() =>
        "einsendungen" + (Slug is null ? "" : "/" + Uri.EscapeDataString(Slug)) + Query(withForm: false);

    /// <summary>
    /// The detail address of one submission, carrying this selection with it. The form travels in the query
    /// here because the detail route addresses the submission, not the form.
    /// </summary>
    public string DetailUrl(string id) => "einsendung/" + Uri.EscapeDataString(id) + Query(withForm: true);

    private string Query(bool withForm)
    {
        var parts = new List<string>(4);
        if (withForm && Slug is not null) parts.Add("formular=" + Uri.EscapeDataString(Slug));
        if (Quick is not null) parts.Add("filter=" + Uri.EscapeDataString(Quick));
        if (Assignee is not null) parts.Add("bearbeiter=" + Uri.EscapeDataString(Assignee));
        if (Page > 1) parts.Add("seite=" + Page.ToString(CultureInfo.InvariantCulture));
        return parts.Count == 0 ? "" : "?" + string.Join("&", parts);
    }

    private static string? Set(string? value) => string.IsNullOrEmpty(value) ? null : value;

    /// <summary>
    /// Names the selection the back link returns to (AC3). The form name is data and stays untranslated;
    /// everything around it is interface language. Without a form the label says "all submissions" rather
    /// than naming the form picker, because that is where the link actually leads.
    ///
    /// A form whose name the caller cannot resolve - the list of forms not loaded yet, or a slug it no
    /// longer has - falls back to the slug, never to "all submissions": the label must not claim a wider
    /// selection than the link beneath it leads to.
    /// </summary>
    public string BackLabel(Ui t, string? formName)
    {
        var target = formName ?? Slug ?? t["allen Einsendungen"];
        var narrowings = new List<string>(2);
        if (AssigneeLabel(t) is { } who) narrowings.Add(who);
        if (QuickLabel(t) is { } quick) narrowings.Add(quick);
        return narrowings.Count == 0
            ? t.F("Zurück zu {0}", target)
            : t.F("Zurück zu {0} · {1}", target, string.Join(" · ", narrowings));
    }

    /// <summary>
    /// The personal filter as the list writes it (#13). The admin's name is data and stays untranslated,
    /// exactly like the form name above.
    /// </summary>
    private string? AssigneeLabel(Ui t) => Assignee switch
    {
        null => null,
        Nobody => t["Offene ohne Bearbeiter"],
        var who => t.F("Offene von {0}", who),
    };

    /// <summary>The quick filter as the list writes it; null for none and for one this version does not know.</summary>
    private string? QuickLabel(Ui t) => Quick switch
    {
        "new" => t["Neu seit letztem Besuch"],
        "todo" => t["Zu bearbeiten"],
        "waiting" => t["Wartet auf Bestätigung"],
        "failed" => t["Fehler"],
        _ => null,
    };

    /// <summary>
    /// The submissions of this selection, in the order the list shows them. The incoming order is kept
    /// rather than re-sorted: it is the order the endpoint returns and therefore the order the reader saw,
    /// which is what the arrows have to follow (AC4). The form is filtered here as well, although the
    /// endpoint already narrows by slug - so a caller that loaded every form can still ask for one.
    /// </summary>
    public IReadOnlyList<SubmissionListItem> Select(IReadOnlyList<SubmissionListItem> all, DateTimeOffset? lastVisit) =>
        all.Where(s => Slug is null || s.Slug == Slug)
           // One filter concept, not two (AC4, AC5): an assignee filter always means open *and* assigned
           // to that person. "Meine offenen" is this filter with the signed-in admin's name; the picker
           // is the same filter with someone else's, or with nobody's. Without the open half it would
           // grow into an archive of everything that person ever touched.
           .Where(s => Assignee switch
           {
               null => true,
               Nobody => s.Handling == "open" && s.Assignee is null,
               var who => s.Handling == "open" && s.Assignee == who,
           })
           .Where(s => Quick switch
           {
               "new" => IsNew(s, lastVisit),
               "todo" => s.Handling == "open",
               "waiting" => s.State == 1,
               "failed" => s.State == 2,
               _ => true,
           })
           .ToList();

    /// <summary>
    /// Arrived since the reader last looked. One definition for the marker on the row and for the "new"
    /// filter: two of them drift, and the list then dots rows the filter does not show. A store that has
    /// never been visited counts everything as new.
    /// </summary>
    public static bool IsNew(SubmissionListItem item, DateTimeOffset? lastVisit) =>
        lastVisit is null || item.CreatedAt > lastVisit;

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
        return index < 0 ? Clamped(selection) : this with { Page = index / PerPage + 1 };
    }

    /// <summary>
    /// The same selection on a page that exists. An address can name a page the selection does not have -
    /// hand-edited, shared after the filter moved on, or carried back from a submission that is no longer
    /// in it. Without this the list shows an empty page numbered past its own end, and the only way out is
    /// the back button.
    /// </summary>
    public SubmissionSelection Clamped(IReadOnlyList<SubmissionListItem> selection) =>
        this with { Page = Math.Clamp(Page, 1, PageCount(selection)) };

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
