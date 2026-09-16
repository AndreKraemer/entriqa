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
public sealed record SubmissionSelection(
    string? Slug, string? Quick, int Page, string? Assignee = null,
    string? Search = null, DateOnly? From = null, DateOnly? To = null)
{
    private readonly string? _quick = Canonical(Quick);

    /// <summary>
    /// The chips this selection carries, comma separated. Canonicalised on the way in - by the constructor
    /// and by every <c>with</c> - so that the same set is always the same value: a record compares what it
    /// stores, and two addresses naming the same chips in another order would otherwise be unequal
    /// selections that render identically.
    /// </summary>
    public string? Quick { get => _quick; init => _quick = Canonical(value); }

    public const int PerPage = 15;

    /// <summary>
    /// The assignee filter that means "nobody has taken this" (#13). A single dash can never be an
    /// admin's display name, so it needs no escaping and no second query parameter.
    /// </summary>
    public const string Nobody = "-";

    /// <summary>
    /// The quick filters the list offers, in the order the bar shows them. Since #12 a selection can carry
    /// several at once, and the order is what makes two addresses naming the same chips equal values -
    /// the lit chip, the badge count and the way back all key on record equality.
    /// </summary>
    private static readonly string[] ChipOrder = ["new", "todo", "waiting", "failed"];

    /// <summary>The quick filters the list offers. An address naming anything else has no filter at all.</summary>
    public static readonly IReadOnlySet<string> QuickFilters = new HashSet<string>(ChipOrder, StringComparer.Ordinal);

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
    /// Whether this selection is that admin's personal filter, on whichever page. Paging stays inside the
    /// filter; narrowing does not - the form select and the quick filters deliberately keep the assignee,
    /// so a selection can carry a name without being the cross-form set the chip stands for.
    /// </summary>
    public bool IsPersonal(string who) => this with { Page = 1 } == Personal(who);

    /// <summary>
    /// Choosing "Meine offenen", and choosing it again. It turns on exactly where the chip is unlit, and
    /// then leads into the filter the badge beside it counts; from inside, it gives the whole list back.
    ///
    /// Keyed on <see cref="IsPersonal"/> and not on the assignee alone, or the three would part company:
    /// a selection that merely carries my name - after picking myself in the form list's picker, or after
    /// choosing the chip and then a form - would show an unlit chip, a badge counting the cross-form set,
    /// and a click that dropped the name instead of going to what that badge promised. Removing the name
    /// without leaving the form is what "Alle Bearbeiter" in the picker is for.
    /// </summary>
    public SubmissionSelection TogglePersonal(string who) =>
        IsPersonal(who) ? this with { Assignee = null, Page = 1 } : Personal(who);

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
        var quick = Canonical(q["filter"]);
        // The assignee is not validated against the admin list on purpose: a name the list no longer
        // holds yields an empty selection, which is honest, where silently dropping the narrowing would
        // show a wider list than the address promises.
        return new SubmissionSelection(slug ?? Set(q["formular"]), quick, page, Set(q["bearbeiter"]),
            Set(q["suche"]), Day(q["von"]), Day(q["bis"]));
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
        var parts = new List<string>(7);
        if (withForm && Slug is not null) parts.Add("formular=" + Uri.EscapeDataString(Slug));
        if (Quick is not null) parts.Add("filter=" + Uri.EscapeDataString(Quick));
        if (Assignee is not null) parts.Add("bearbeiter=" + Uri.EscapeDataString(Assignee));
        if (Search is not null) parts.Add("suche=" + Uri.EscapeDataString(Search));
        if (From is { } from) parts.Add("von=" + Iso(from));
        if (To is { } to) parts.Add("bis=" + Iso(to));
        if (Page > 1) parts.Add("seite=" + Page.ToString(CultureInfo.InvariantCulture));
        return parts.Count == 0 ? "" : "?" + string.Join("&", parts);
    }

    private static string? Set(string? value) => string.IsNullOrEmpty(value) ? null : value;

    /// <summary>
    /// A day of the range as the address and the date input both write it. Parsed exactly rather than by
    /// the current culture: the same address has to mean the same range in a German and an English admin.
    /// </summary>
    private static DateOnly? Day(string? value) =>
        DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)
            ? day
            : null;

    private static string Iso(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

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
        narrowings.AddRange(QuickLabels(t));
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

    /// <summary>The quick filters as the list writes them, in the order the bar shows them.</summary>
    private IEnumerable<string> QuickLabels(Ui t) => Chips(Quick).Select(c => c switch
    {
        "new" => t["Neu seit letztem Besuch"],
        "todo" => t["Zu bearbeiten"],
        "waiting" => t["Wartet auf Bestätigung"],
        _ => t["Fehler"],
    });

    /// <summary>
    /// The submissions of this selection, in the order the list shows them. The incoming order is kept
    /// rather than re-sorted: it is the order the endpoint returns and therefore the order the reader saw,
    /// which is what the arrows have to follow (AC4). The form is filtered here as well, although the
    /// endpoint already narrows by slug - so a caller that loaded every form can still ask for one.
    /// </summary>
    public IReadOnlyList<SubmissionListItem> Select(IReadOnlyList<SubmissionListItem> all, DateTimeOffset? lastVisit,
                                                   TimeZoneInfo? zone = null) =>
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
           .Where(s => MatchesChips(Chips(Quick), s, lastVisit))
           .Where(s => InRange(s.CreatedAt, zone ?? TimeZoneInfo.Local))
           .ToList();

    /// <summary>
    /// Whether a submission falls into the chosen range, both ends included. Compared on the local day,
    /// because that is the day the list prints beside it - judging by UTC would drop an evening enquiry
    /// out of the day its reader saw it arrive. The zone is an argument so the tests do not depend on
    /// where the suite runs.
    /// </summary>
    private bool InRange(DateTimeOffset at, TimeZoneInfo zone)
    {
        if (From is null && To is null) return true;
        var day = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(at, zone).DateTime);
        return (From is not { } from || day >= from) && (To is not { } to || day <= to);
    }

    /// <summary>
    /// Arrived since the reader last looked. One definition for the marker on the row and for the "new"
    /// filter: two of them drift, and the list then dots rows the filter does not show. A store that has
    /// never been visited counts everything as new.
    /// </summary>
    public static bool IsNew(SubmissionListItem item, DateTimeOffset? lastVisit) =>
        lastVisit is null || item.CreatedAt > lastVisit;

    /// <summary>
    /// Whether this selection carries that chip (#12). The chips are a set rather than one slot since
    /// AC4 - Verarbeitungsstatus, Bearbeitungsstatus, Zeitraum and Bearbeiter have to combine - so the
    /// single string now holds them comma separated in a canonical order.
    /// </summary>
    public bool HasChip(string chip) => Chips(Quick).Contains(chip, StringComparer.Ordinal);

    /// <summary>The same selection with that chip switched on or off, back on page one.</summary>
    public SubmissionSelection ToggleChip(string chip)
    {
        var chips = Chips(Quick).ToList();
        if (!chips.Remove(chip) && QuickFilters.Contains(chip)) chips.Add(chip);
        return this with { Quick = Canonical(string.Join(',', chips)), Page = 1 };
    }

    /// <summary>
    /// The chips of a filter value, unknown ones dropped, duplicates gone and in canonical order. An
    /// address is something people edit and share, so it must survive that - and a chip the list does not
    /// offer would otherwise claim a narrowing nothing applies.
    /// </summary>
    private static string[] Chips(string? quick)
    {
        if (quick is null) return [];
        var named = quick.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return [.. ChipOrder.Where(c => named.Contains(c, StringComparer.Ordinal))];
    }

    private static string? Canonical(string? quick) =>
        Chips(quick) is { Length: > 0 } chips ? string.Join(',', chips) : null;

    /// <summary>
    /// Whether a submission survives the chips (AC4). Two chips of the same dimension widen - no submission
    /// is waiting and failed at once, so reading them as two conditions at the same time would make the
    /// second click empty the list. Chips of different dimensions narrow, which is the criterion's claim.
    /// </summary>
    private static bool MatchesChips(string[] chips, SubmissionListItem s, DateTimeOffset? lastVisit)
    {
        if (chips.Length == 0) return true;
        var states = chips.Where(c => c is "waiting" or "failed").ToArray();
        if (states.Length > 0 && !states.Any(c => c == "waiting" ? s.State == 1 : s.State == 2)) return false;
        if (chips.Contains("todo", StringComparer.Ordinal) && s.Handling != "open") return false;
        if (chips.Contains("new", StringComparer.Ordinal) && !IsNew(s, lastVisit)) return false;
        return true;
    }

    /// <summary>
    /// Whether moving from <paramref name="previous"/> to this selection needs a new request (#12, AC8).
    /// Only the form and the search term decide what the endpoint returns; every other narrowing happens
    /// on what is already here, so turning a chip on must not cost a round trip.
    /// </summary>
    public bool NeedsReload(SubmissionSelection previous) =>
        Slug != previous.Slug || !string.Equals(Search, previous.Search, StringComparison.Ordinal);

    /// <summary>
    /// What the list says about the search it is showing (AC5, AC6): how many submissions were looked at,
    /// and - when the ceiling stopped the scan - that older ones were never searched. Null when no search
    /// is running, which is what keeps the line out of the ordinary list.
    /// </summary>
    public string? SearchSummary(Ui t, int scanned, bool capped) => Search is null
        ? null
        : capped
            ? t.F("Die neuesten {0} Einsendungen durchsucht – ältere wurden nicht einbezogen.", scanned)
            : t.F("{0} Einsendungen durchsucht.", scanned);

    /// <summary>The empty list's message. During a search it names the term (AC7).</summary>
    public string EmptyMessage(Ui t) => Search is { } term
        ? t.F("Nichts gefunden für „{0}“.", term)
        : t["Nichts in dieser Auswahl."];

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
