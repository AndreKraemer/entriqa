using Entriqa.Domain.Forms;
using Entriqa.Domain.Submissions;

namespace Entriqa.Domain.UseCases;

// Public use cases (anonymous, called by forms.js)

public interface IGetPublishedFormUseCase
{
    /// <summary>Returns the resolved, single-language view; unknown language -> default locale of the definition.</summary>
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
    /// <summary>Runs pending background steps. Authorized through the run token from the submit response.</summary>
    Task ExecuteAsync(string submissionId, string runToken, CancellationToken ct = default);
}

public interface IConfirmSubmissionUseCase
{
    /// <summary>DOI confirmation: checks the token from the mail, marks the submission as confirmed and starts the second phase.</summary>
    Task<ConfirmResult> ExecuteAsync(string confirmToken, string? clientIp, CancellationToken ct = default);
}

public sealed record ConfirmResult(string Slug, string RedirectUrl, bool AlreadyConfirmed);

/// <summary>Housekeeping (every 15 min via DevOps schedule): deferred sweep, auto retry, retention, cleaning up the security tables.</summary>
public interface IRunHousekeepingUseCase
{
    Task<HousekeepingResult> ExecuteAsync(CancellationToken ct = default);
}

public sealed record HousekeepingResult(int Swept, int Retried, int Deleted, int NoncesPurged, int RateLimitsPurged);

// Admin use cases (role "admin", called by the Blazor admin)

public interface IListRecentSubmissionsUseCase
{
    /// <summary>Latest submissions (optionally per form) together with the calling admin's last visit - for "new since" markers.</summary>
    Task<RecentSubmissions> ExecuteAsync(string? slug, string user, int max = 500, CancellationToken ct = default);
}

public sealed record RecentSubmissions(IReadOnlyList<SubmissionListItem> Items, DateTimeOffset? LastVisitAt);

public interface IMarkVisitedUseCase
{
    /// <summary>Sets the admin's "last visit" to now. Afterwards nothing counts as new any more.</summary>
    Task<DateTimeOffset> ExecuteAsync(string user, CancellationToken ct = default);
}

public interface IGetIntegrationDirectoryUseCase
{
    /// <summary>Lookups for the builder: Brevo lists and templates, ReportingCloud templates, uploaded lead magnets. Empty without keys, but without an error.</summary>
    Task<IntegrationDirectory> ExecuteAsync(CancellationToken ct = default);
}

public sealed record IntegrationDirectory(
    bool BrevoConfigured, IReadOnlyList<DirectoryEntry> BrevoLists, IReadOnlyList<DirectoryEntry> BrevoTemplates,
    bool ReportingCloudConfigured, IReadOnlyList<string> ReportTemplates,
    IReadOnlyList<LeadMagnetInfo> LeadMagnets,
    // False when that section could not be loaded - an incomplete directory must never look complete.
    bool BrevoListsComplete = true, bool BrevoTemplatesComplete = true);

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
    /// <summary>Sends the DOI confirmation mail again (only while it is unconfirmed).</summary>
    Task ExecuteAsync(string submissionId, CancellationToken ct = default);
}

public interface IGetFormStatsUseCase
{
    /// <summary>Statistics for a form: 14-day history, quiz, lead-magnet and choice-field distributions. <paramref name="version"/> null = all.</summary>
    Task<FormStats> ExecuteAsync(string slug, int? version, CancellationToken ct = default);
}

public sealed record FormStats(
    int Total,
    IReadOnlyList<int> Daily,                              // 14 entries, [0] = 13 days ago … [13] = today (UTC)
    IReadOnlyList<int> Versions,                           // versions present (for the filter)
    int WithEmail,
    int Confirmed,                                         // DOI confirmed
    int AwaitingConfirmation,
    int Failed,
    IReadOnlyList<StatsBar> Sources,                       // utm_source
    QuizStats? Quiz,
    IReadOnlyList<FieldStats> SelectFields,
    FunnelStats? Funnel = null);                           // aggregated view and start counters (14 days, no personal data)

public sealed record FunnelStats(int Views, int Starts);

public interface ICountFormEventUseCase
{
    /// <summary>Counts a form event (view | start) as a daily counter - without ids, without cookies, without content.</summary>
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
    /// <summary>Current draft (identical to the latest version right after publishing). 404 when unknown.</summary>
    Task<FormDraftView> ExecuteAsync(string slug, CancellationToken ct = default);
}

public sealed record FormDraftView(string Slug, string Status, int PublishedVersion, DateTimeOffset UpdatedAt, string UpdatedBy, FormDefinition Definition);

public interface ISaveFormDraftUseCase
{
    Task ExecuteAsync(FormDefinition definition, string savedBy, CancellationToken ct = default);
}

public interface IPublishFormUseCase
{
    /// <summary>Checks the draft and publishes it as a new version. Issues not empty -> not published (version 0).</summary>
    Task<PublishFormResult> ExecuteAsync(string slug, string publishedBy, CancellationToken ct = default);
}

public sealed record PublishFormResult(int Version, IReadOnlyList<string> Issues);

public interface IGetSubmissionDetailUseCase
{
    Task<SubmissionDetailView> ExecuteAsync(string submissionId, CancellationToken ct = default);
}

/// <summary>Detail view without IP hashes - those are evidence, not UI material.</summary>
public sealed record SubmissionDetailView(
    string Id, string Slug, int Version, DateTimeOffset CreatedAt, string? Locale, string? Email, string? FirstName,
    string? Source, IReadOnlyList<SubmissionValueView> Values, Quiz.QuizOutcome? Quiz, string? QuizResultTitle,
    string? ConsentText, DateTimeOffset? ConfirmedAt, IReadOnlyList<StepRun> StepRuns, string Handling, SubmissionState State,
    string? BrevoContactId, bool CanResendDoi, IReadOnlyList<QuizAnswerView>? QuizAnswers = null);

public sealed record QuizAnswerView(string Question, string Answer, int Points, bool Jumped);

public interface IGetFormsActivityUseCase
{
    /// <summary>Per form: total count and submissions of the last 14 days - for the form list.</summary>
    Task<IReadOnlyDictionary<string, FormActivity>> ExecuteAsync(CancellationToken ct = default);
}

public sealed record FormActivity(int Total, IReadOnlyList<int> Daily);

// Contact view: who has ever submitted and what came in per contact or company - limited by the retention period.

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
    /// <summary>GDPR deletion: removes every submission of that address (blobs included). Returns the count.</summary>
    /// <param name="by">The admin account that triggered it - it lands in the erasure audit row (#2).</param>
    Task<int> ExecuteAsync(string email, string by, CancellationToken ct = default);
}

// Consent proofs in the admin (#2): the window onto what #1 files away. Searchable by address so an
// Art. 15 enquiry can be answered, and removable one at a time when a single consent is revoked.

/// <summary>
/// One proof as the admin reads it. It carries the wording that was ticked, never the form's current
/// text - the evidence is what the visitor agreed to, not what the form says today (AC 5).
/// </summary>
public sealed record ConsentProofView(string Email, string SubmissionId, string Slug, int Version,
    DateTimeOffset SubmittedAt, DateTimeOffset? ConfirmedAt, string ConsentText,
    string? IpHash, string? ConfirmedIpHash);

public interface IListConsentProofsUseCase
{
    /// <summary>Every proof of that address, newest submission first. Empty when there is none.</summary>
    Task<IReadOnlyList<ConsentProofView>> ExecuteAsync(string email, CancellationToken ct = default);
}

public interface IDeleteConsentProofUseCase
{
    /// <summary>Removes one proof and records the erasure. false when there was nothing under that id.</summary>
    Task<bool> ExecuteAsync(string email, string submissionId, string by, CancellationToken ct = default);
}

public interface IUploadFileUseCase
{
    /// <summary>Takes a visitor file (whitelist, size limit) and stores it privately under uploads/.</summary>
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

/// <summary>Status of the production license: valid | expired | invalid | missing.</summary>
public sealed record LicenseView(bool Valid, string Status, string? Plan, DateOnly? ValidUntil);

public sealed record SubmissionValueView(string FieldId, string Label, string Value);

public interface ISetSubmissionHandlingUseCase
{
    Task ExecuteAsync(string submissionId, string handling, CancellationToken ct = default);   // open | done
}

public interface IDeleteSubmissionAdminUseCase
{
    /// <summary>Deletes the submission including its report blobs (GDPR request, test data).</summary>
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

/// <summary>Describes a step for the admin catalog; <c>ConfigSchema</c> is JSON Schema (draft-07) from which the builder renders the configuration form.</summary>
public sealed record StepDescriptor(string Key, string Name, string Description, string Mode, bool SplitsPhase,
    IReadOnlyList<string> Needs, string? Produces, string ConfigSchema, bool CriticalByDefault = true,
    IReadOnlyList<MailParam>? MailParams = null);

/// <summary>A variable that a step passes to Brevo templates - available in the template as <c>{{ params.Name }}</c>.</summary>
public sealed record MailParam(string Name, string Description);
