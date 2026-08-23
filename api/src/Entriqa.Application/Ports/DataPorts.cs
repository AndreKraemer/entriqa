using Entriqa.Domain.Forms;
using Entriqa.Domain.Submissions;

namespace Entriqa.Application.Ports;

// Lese-/Schreib-Ports der Data-Schicht. Keine Repositories: ein Port = eine Absicht (Solution Standard §13/§14).

public interface ITryGetPublishedFormQuery
{
    Task<FormVersion?> ExecuteAsync(string slug, CancellationToken ct = default);
}

public interface ITryGetFormVersionQuery
{
    Task<FormVersion?> ExecuteAsync(string slug, int version, CancellationToken ct = default);
}

public interface IStoreSubmissionCommand
{
    /// <summary>Legt die Einsendung an. Atomar: ein Upsert, ein Entity.</summary>
    Task ExecuteAsync(Submission submission, CancellationToken ct = default);
}

public interface ISaveSubmissionCommand
{
    /// <summary>Schreibt Schrittstatus, Artefakte, Bestätigung zurück. Atomar wie oben.</summary>
    Task ExecuteAsync(Submission submission, CancellationToken ct = default);
}

public interface ITryGetSubmissionQuery
{
    Task<Submission?> ExecuteAsync(string submissionId, CancellationToken ct = default);
}

public interface IListSubmissionsQuery
{
    Task<SubmissionPage> ExecuteAsync(string slug, string? continuationToken, int pageSize, CancellationToken ct = default);
}

public interface ITryConsumeNonceCommand
{
    /// <summary>true beim ersten Mal, false wenn die Nonce schon verbraucht war (Replay).</summary>
    Task<bool> ExecuteAsync(string nonce, DateTimeOffset expiresAt, CancellationToken ct = default);
}

public interface IRegisterRateLimitHitCommand
{
    /// <summary>Zählt einen Treffer für (ipHash, Zeitfenster) und gibt die neue Anzahl zurück.</summary>
    Task<int> ExecuteAsync(string ipHash, DateTimeOffset windowStart, CancellationToken ct = default);
}

// Funnel-Zähler (Abbruch-Analytics ohne Personenbezug: nur Tages-Summen je Formular)

public interface IIncrementFunnelCommand
{
    Task ExecuteAsync(string slug, DateOnly day, string type, CancellationToken ct = default);
}

public interface IGetFunnelTotalsQuery
{
    /// <summary>Summen je Ereignistyp ab <paramref name="from"/> (einschließlich).</summary>
    Task<IReadOnlyDictionary<string, int>> ExecuteAsync(string slug, DateOnly from, CancellationToken ct = default);
}

// Kontakt-Sicht (Scan über die Einsendungen – Volumen klein und durch die Aufbewahrungsfrist begrenzt)

public interface IListContactSubmissionsQuery
{
    /// <summary>Alle Einsendungen MIT E-Mail-Adresse (vollständig, für die Aggregation je Kontakt/Firma).</summary>
    Task<IReadOnlyList<Submission>> ListWithEmailAsync(int max, CancellationToken ct = default);
    /// <summary>Alle Einsendungen einer Adresse (ohne Groß/Klein), neueste zuerst.</summary>
    Task<IReadOnlyList<SubmissionListItem>> ListByEmailAsync(string email, CancellationToken ct = default);
}

// Admin-Übersicht

public interface IListRecentSubmissionsQuery
{
    /// <summary>Neueste Einsendungen, optional auf ein Formular begrenzt; sortiert nach Eingang absteigend.</summary>
    Task<IReadOnlyList<SubmissionListItem>> ExecuteAsync(string? slug, int max, CancellationToken ct = default);
}

public interface IListSubmissionsForStatsQuery
{
    /// <summary>Alle Einsendungen eines Formulars als Domänenobjekte – für Auswertungen und CSV-Export.</summary>
    Task<IReadOnlyList<Submission>> ExecuteAsync(string slug, int max, CancellationToken ct = default);
}

public interface IGetLastVisitQuery
{
    Task<DateTimeOffset?> ExecuteAsync(string user, CancellationToken ct = default);
}

public interface ISetLastVisitCommand
{
    Task ExecuteAsync(string user, DateTimeOffset at, CancellationToken ct = default);
}

public interface IGetFormsActivityQuery
{
    Task<IReadOnlyDictionary<string, Domain.UseCases.FormActivity>> ExecuteAsync(DateTimeOffset today, CancellationToken ct = default);
}

public interface IRecordHousekeepingRunCommand
{
    Task ExecuteAsync(DateTimeOffset at, string summary, CancellationToken ct = default);
}

public interface IGetHousekeepingRunQuery
{
    Task<(DateTimeOffset At, string Summary)?> ExecuteAsync(CancellationToken ct = default);
}

// Housekeeping-Ports

public interface IListHousekeepingSubmissionsQuery
{
    /// <summary>Unfertige Einsendungen (processing/failed), älter als die Schonfrist – für Sweep und Auto-Retry.</summary>
    Task<IReadOnlyList<Submission>> ListUnfinishedAsync(DateTimeOffset olderThan, int max, CancellationToken ct = default);

    /// <summary>Abgelaufene Einsendungen: generell älter als <paramref name="generalCutoff"/>, unbestätigte DOI älter als <paramref name="unconfirmedCutoff"/>.</summary>
    Task<IReadOnlyList<Submission>> ListExpiredAsync(DateTimeOffset generalCutoff, DateTimeOffset unconfirmedCutoff, int max, CancellationToken ct = default);
}

public interface IDeleteSubmissionCommand
{
    Task ExecuteAsync(Submission submission, CancellationToken ct = default);
}

public interface IPurgeSecurityEntriesCommand
{
    /// <summary>Löscht abgelaufene Nonces und alte Rate-Limit-Fenster. Liefert die Anzahl je Tabelle.</summary>
    Task<(int Nonces, int RateLimits)> ExecuteAsync(DateTimeOffset now, CancellationToken ct = default);
}

// Admin-Ports (Entwurf/Version) – für den Blazor-Admin.

public interface IPublishFormVersionCommand
{
    Task<FormVersion> ExecuteAsync(FormDefinition definition, string publishedBy, CancellationToken ct = default);
}

public interface IListFormsQuery
{
    Task<IReadOnlyList<Domain.UseCases.FormListItem>> ExecuteAsync(CancellationToken ct = default);
}

public sealed record FormDraft(string Slug, string Status, int PublishedVersion, DateTimeOffset UpdatedAt, string UpdatedBy, FormDefinition Definition);

public interface ITryGetFormDraftQuery
{
    Task<FormDraft?> ExecuteAsync(string slug, CancellationToken ct = default);
}

public interface ISaveFormDraftCommand
{
    /// <summary>Legt den Forms-Eintrag an oder aktualisiert den Entwurf; der Status bleibt unberührt (neu = draft).</summary>
    Task ExecuteAsync(FormDefinition definition, string savedBy, CancellationToken ct = default);
}
