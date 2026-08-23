using Entriqa.Domain.Forms;
using Entriqa.Domain.Submissions;

namespace Entriqa.Domain.UseCases;

// Öffentliche Use Cases (anonym, von forms.js aufgerufen)

public interface IGetPublishedFormUseCase
{
    /// <summary>Liefert die aufgelöste, einsprachige Sicht; unbekannte Sprache → Standard-Locale der Definition.</summary>
    Task<PublicFormView> ExecuteAsync(string slug, string? lang = null, CancellationToken ct = default);
}

public interface IIssueFormTokenUseCase
{
    Task<string> ExecuteAsync(string slug, CancellationToken ct = default);
}

public interface ISubmitFormUseCase
{
    Task<SubmitFormResult> ExecuteAsync(SubmitFormRequest request, CancellationToken ct = default);
}

public interface IRunPendingStepsUseCase
{
    /// <summary>Führt ausstehende Hintergrund-Schritte aus. Autorisiert über den Run-Token aus der Submit-Antwort.</summary>
    Task ExecuteAsync(string submissionId, string runToken, CancellationToken ct = default);
}

public interface IConfirmSubmissionUseCase
{
    /// <summary>DOI-Bestätigung: prüft den Token aus der Mail, markiert die Einsendung als bestätigt und startet die zweite Phase.</summary>
    Task<ConfirmResult> ExecuteAsync(string confirmToken, string? clientIp, CancellationToken ct = default);
}

public sealed record ConfirmResult(string Slug, string RedirectUrl, bool AlreadyConfirmed);

/// <summary>Housekeeping (per DevOps-Schedule alle 15 min): Deferred-Sweep, Auto-Retry, Retention, Security-Tabellen aufräumen.</summary>
public interface IRunHousekeepingUseCase
{
    Task<HousekeepingResult> ExecuteAsync(CancellationToken ct = default);
}

public sealed record HousekeepingResult(int Swept, int Retried, int Deleted, int NoncesPurged, int RateLimitsPurged);

// Admin-Use-Cases (Rolle "admin", vom Blazor-Admin aufgerufen)

public interface IListRecentSubmissionsUseCase
{
    /// <summary>Neueste Einsendungen (optional je Formular) samt letztem Besuch des aufrufenden Admins – für „neu seit"-Markierungen.</summary>
    Task<RecentSubmissions> ExecuteAsync(string? slug, string user, int max = 500, CancellationToken ct = default);
}

public sealed record RecentSubmissions(IReadOnlyList<SubmissionListItem> Items, DateTimeOffset? LastVisitAt);

public interface IMarkVisitedUseCase
{
    /// <summary>Setzt „letzter Besuch" des Admins auf jetzt. Danach zählt nichts mehr als neu.</summary>
    Task<DateTimeOffset> ExecuteAsync(string user, CancellationToken ct = default);
}

public interface IGetIntegrationDirectoryUseCase
{
    /// <summary>Lookups für den Builder: Brevo-Listen/-Vorlagen, ReportingCloud-Vorlagen, hochgeladene Lead-Magnete. Ohne Keys leer, aber ohne Fehler.</summary>
    Task<IntegrationDirectory> ExecuteAsync(CancellationToken ct = default);
}

public sealed record IntegrationDirectory(
    bool BrevoConfigured, IReadOnlyList<DirectoryEntry> BrevoLists, IReadOnlyList<DirectoryEntry> BrevoTemplates,
    bool ReportingCloudConfigured, IReadOnlyList<string> ReportTemplates,
    IReadOnlyList<LeadMagnetInfo> LeadMagnets);

public sealed record DirectoryEntry(long Id, string Name);
public sealed record LeadMagnetInfo(string Path, long Size);

public interface IUploadLeadMagnetUseCase
{
    Task<LeadMagnetInfo> ExecuteAsync(string fileName, byte[] content, string contentType, CancellationToken ct = default);
}

public interface IExportSubmissionsCsvUseCase
{
    Task<string> ExecuteAsync(string slug, CancellationToken ct = default);
}

public interface IResendDoiUseCase
{
    /// <summary>Verschickt die DOI-Bestätigungsmail erneut (nur solange unbestätigt).</summary>
    Task ExecuteAsync(string submissionId, CancellationToken ct = default);
}

public interface IGetFormStatsUseCase
{
    /// <summary>Auswertung eines Formulars: 14-Tage-Verlauf, Quiz-/Lead-Magnet-/Auswahlfeld-Verteilungen. <paramref name="version"/> null = alle.</summary>
    Task<FormStats> ExecuteAsync(string slug, int? version, CancellationToken ct = default);
}

public sealed record FormStats(
    int Total,
    IReadOnlyList<int> Daily,                              // 14 Einträge, [0] = vor 13 Tagen … [13] = heute (UTC)
    IReadOnlyList<int> Versions,                           // vorhandene Versionen (für den Filter)
    int WithEmail,
    int Confirmed,                                         // DOI bestätigt
    int AwaitingConfirmation,
    int Failed,
    IReadOnlyList<StatsBar> Sources,                       // utm_source
    QuizStats? Quiz,
    IReadOnlyList<FieldStats> SelectFields,
    FunnelStats? Funnel = null);                           // aggregierte Aufruf-/Start-Zähler (14 Tage, ohne Personenbezug)

public sealed record FunnelStats(int Views, int Starts);

public interface ICountFormEventUseCase
{
    /// <summary>Zählt ein Formular-Ereignis (view | start) als Tageszähler – ohne IDs, ohne Cookies, ohne Inhalte.</summary>
    Task ExecuteAsync(string slug, string type, CancellationToken ct = default);
}

public sealed record StatsBar(string Label, int Count);
public sealed record QuizStats(IReadOnlyList<StatsBar> Results, int AvgPct, int EndedByJump, IReadOnlyList<QuestionStats> Questions);
public sealed record QuestionStats(string Question, int Seen, IReadOnlyList<StatsBar> Options);
public sealed record FieldStats(string Label, IReadOnlyList<StatsBar> Options);

public interface IListFormsUseCase
{
    Task<IReadOnlyList<FormListItem>> ExecuteAsync(CancellationToken ct = default);
}

public sealed record FormListItem(string Slug, string Name, string Type, string Status, int PublishedVersion, DateTimeOffset UpdatedAt);

public interface IGetFormDraftUseCase
{
    /// <summary>Aktueller Entwurf (nach dem Veröffentlichen identisch mit der letzten Version). 404 wenn unbekannt.</summary>
    Task<FormDraftView> ExecuteAsync(string slug, CancellationToken ct = default);
}

public sealed record FormDraftView(string Slug, string Status, int PublishedVersion, DateTimeOffset UpdatedAt, string UpdatedBy, FormDefinition Definition);

public interface ISaveFormDraftUseCase
{
    Task ExecuteAsync(FormDefinition definition, string savedBy, CancellationToken ct = default);
}

public interface IPublishFormUseCase
{
    /// <summary>Prüft den Entwurf und veröffentlicht ihn als neue Version. Issues ≠ leer → nicht veröffentlicht (Version 0).</summary>
    Task<PublishFormResult> ExecuteAsync(string slug, string publishedBy, CancellationToken ct = default);
}

public sealed record PublishFormResult(int Version, IReadOnlyList<string> Issues);

public interface IGetSubmissionDetailUseCase
{
    Task<SubmissionDetailView> ExecuteAsync(string submissionId, CancellationToken ct = default);
}

/// <summary>Detailansicht ohne IP-Hashes – die sind Nachweis, kein UI-Material.</summary>
public sealed record SubmissionDetailView(
    string Id, string Slug, int Version, DateTimeOffset CreatedAt, string? Locale, string? Email, string? FirstName,
    string? Source, IReadOnlyList<SubmissionValueView> Values, Quiz.QuizOutcome? Quiz, string? QuizResultTitle,
    string? ConsentText, DateTimeOffset? ConfirmedAt, IReadOnlyList<StepRun> StepRuns, string Handling, SubmissionState State,
    string? BrevoContactId, bool CanResendDoi, IReadOnlyList<QuizAnswerView>? QuizAnswers = null);

public sealed record QuizAnswerView(string Question, string Answer, int Points, bool Jumped);

public interface IGetFormsActivityUseCase
{
    /// <summary>Je Formular: Gesamtzahl und Einsendungen der letzten 14 Tage – für die Formularliste.</summary>
    Task<IReadOnlyDictionary<string, FormActivity>> ExecuteAsync(CancellationToken ct = default);
}

public sealed record FormActivity(int Total, IReadOnlyList<int> Daily);

// Kontakt-Sicht: wer hat je eingesendet, was kam je Kontakt/Firma – begrenzt durch die Aufbewahrungsfrist.

public interface IListContactsUseCase
{
    Task<IReadOnlyList<ContactSummary>> ExecuteAsync(CancellationToken ct = default);
}

public sealed record ContactSummary(string Email, string? Name, string? Company, int Count,
    DateTimeOffset FirstAt, DateTimeOffset LastAt, string? BrevoContactId, IReadOnlyList<string> Slugs);

public interface IListContactSubmissionsUseCase
{
    Task<IReadOnlyList<SubmissionListItem>> ExecuteAsync(string email, CancellationToken ct = default);
}

public interface IDeleteContactUseCase
{
    /// <summary>DSGVO-Löschung: entfernt alle Einsendungen der Adresse (samt Blobs). Gibt die Anzahl zurück.</summary>
    Task<int> ExecuteAsync(string email, CancellationToken ct = default);
}

public interface IUploadFileUseCase
{
    /// <summary>Nimmt eine Besucher-Datei entgegen (Whitelist, Größenlimit) und legt sie privat unter uploads/ ab.</summary>
    Task<UploadedFile> ExecuteAsync(string slug, string token, string fileName, byte[] content, CancellationToken ct = default);
}

public sealed record UploadedFile(string Path, string Name, long Size);

public interface IGetAdminStatusUseCase
{
    Task<AdminStatus> ExecuteAsync(CancellationToken ct = default);
}

public sealed record AdminStatus(
    string SiteName, string BaseUrl,
    bool BrevoConfigured, bool BrevoOk,
    bool ReportingCloudConfigured,
    int RetentionDays, int UnconfirmedRetentionDays, int SweepAfterMinutes, int AutoRetryMax,
    DateTimeOffset? HousekeepingLastRunAt, string? HousekeepingSummary,
    IReadOnlyList<string> Locales,
    LicenseView License);

/// <summary>Status der Produktivlizenz: valid | expired | invalid | missing.</summary>
public sealed record LicenseView(bool Valid, string Status, string? Plan, DateOnly? ValidUntil);

public sealed record SubmissionValueView(string FieldId, string Label, string Value);

public interface ISetSubmissionHandlingUseCase
{
    Task ExecuteAsync(string submissionId, string handling, CancellationToken ct = default);   // open | done
}

public interface IDeleteSubmissionAdminUseCase
{
    /// <summary>Löscht die Einsendung samt Report-Blobs (DSGVO-Löschwunsch, Testdaten).</summary>
    Task ExecuteAsync(string submissionId, CancellationToken ct = default);
}

public interface IListSubmissionsUseCase
{
    Task<SubmissionPage> ExecuteAsync(string slug, string? continuationToken, int pageSize = 25, CancellationToken ct = default);
}

public interface IRetryStepUseCase
{
    Task ExecuteAsync(string submissionId, string stepId, CancellationToken ct = default);
}

public interface IGetStepCatalogUseCase
{
    Task<IReadOnlyList<StepDescriptor>> ExecuteAsync(CancellationToken ct = default);
}

public interface ICheckFormForPublishUseCase
{
    Task<IReadOnlyList<string>> ExecuteAsync(FormDefinition definition, CancellationToken ct = default);
}

/// <summary>Beschreibt einen Schritt für den Admin-Katalog; <c>ConfigSchema</c> ist JSON Schema (draft-07), aus dem der Builder das Konfigurationsformular rendert.</summary>
public sealed record StepDescriptor(string Key, string Name, string Description, string Mode, bool SplitsPhase,
    IReadOnlyList<string> Needs, string? Produces, string ConfigSchema, bool CriticalByDefault = true);
