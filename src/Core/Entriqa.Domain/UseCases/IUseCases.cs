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

/// <summary><paramref name="IsTest"/> is true when the confirm link came from a test mail (#21): the page
/// then shows a hint that it belonged to a test, and nothing is looked up or confirmed.</summary>
public sealed record ConfirmResult(string Slug, string RedirectUrl, bool AlreadyConfirmed, bool IsTest = false);

// Test mode (#21): run a draft through the pipeline without side effects.

/// <summary>
/// Runs a draft through validation, quiz scoring and every pipeline step without any side effect (#21):
/// the draft is loaded (never the published version), mail steps go to the signed-in admin, steps with an
/// external write are suppressed and describe what they would have done, and nothing is ever stored.
/// </summary>
public interface IRunFormTestUseCase
{
    Task<FormTestResult> ExecuteAsync(FormTestRequest request, CancellationToken ct = default);
}

/// <summary>The draft as the single-language view forms.js renders (#21, AC1) - the draft counterpart of
/// <see cref="IGetPublishedFormUseCase"/>, so the test form looks exactly like the visitor's.</summary>
public interface IGetDraftFormViewUseCase
{
    Task<PublicFormView> ExecuteAsync(string slug, string? lang = null, CancellationToken ct = default);
}

/// <summary><paramref name="AdminEmail"/> is where mail steps are redirected; null when the principal carried none.</summary>
public sealed record FormTestRequest(string Slug, string? Lang, Dictionary<string, string> Values, Dictionary<string, string>? Answers, string? AdminEmail);

/// <summary>The protocol of a test run: one verdict per step, plus the address mail steps were sent to.</summary>
public sealed record FormTestResult(IReadOnlyList<TestStepOutcome> Steps, string? MailTo);

/// <summary>One step's verdict in a test run (#21): whether it would have run, was skipped or failed, with resolved values.</summary>
public sealed record TestStepOutcome(string StepId, string StepKey, StepRunStatus Status, string? Error, IReadOnlyList<TestNote> Notes)
{
    /// <summary>The status as a stable string for the JS protocol view - the enum itself serializes as a number.</summary>
    public string StatusName => Status.ToString();
}

/// <summary>A resolved value a suppressed step would have used - a list, template, file or target address (#21).</summary>
public sealed record TestNote(string Label, string Value);

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

public interface ISearchSubmissionsUseCase
{
    /// <summary>
    /// The submissions whose e-mail, name or field values contain <paramref name="term"/>, newest first,
    /// out of at most <paramref name="scanMax"/> scanned rows (#12). The last visit travels with them
    /// exactly as it does for the plain inbox, so a hit list can still mark what is new.
    /// </summary>
    Task<SubmissionSearchResult> ExecuteAsync(string? slug, string term, string user, int scanMax = 5000, CancellationToken ct = default);
}

/// <summary>
/// A hit list together with what it cost: <paramref name="Scanned"/> is what AC5 shows the reader, and
/// <paramref name="Capped"/> says the ceiling cut the scan short, which AC6 forbids hiding.
/// </summary>
public sealed record SubmissionSearchResult(
    IReadOnlyList<SubmissionListItem> Items, DateTimeOffset? LastVisitAt, int Scanned, bool Capped);

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
    /// <param name="by">The signed-in admin, null when the request carried no identity (#14).</param>
    Task ExecuteAsync(string submissionId, string? by, CancellationToken ct = default);
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

public interface IGetOverallStatsUseCase
{
    /// <summary>
    /// Statistics across all published forms - the view shown when no single form is chosen (#16).
    /// Carries no quiz metrics: questions, options and results differ per form and do not compare across forms.
    /// </summary>
    Task<OverallStats> ExecuteAsync(CancellationToken ct = default);
}

public sealed record OverallStats(
    int Total,                                             // all-time submissions across all forms
    IReadOnlyList<int> Daily,                              // 14 entries, [0] = 13 days ago … [13] = today (UTC), summed over all forms
    int WithEmail,
    int Confirmed,                                         // DOI confirmed
    int AwaitingConfirmation,
    int Failed,
    IReadOnlyList<StatsBar> Sources,                       // utm_source across all forms
    IReadOnlyList<FormBreakdown> Forms);                   // every published form, submissions descending; empty = no published form

public sealed record FormBreakdown(
    string Slug,
    string Name,
    int Total,                                             // all-time submissions of this form
    int Recent,                                            // submissions in the last 14 days (for the completion rate)
    int Views,                                             // funnel views, last 14 days
    int Starts);                                           // funnel starts, last 14 days

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
    string? ConsentText, DateTimeOffset? ConfirmedAt, IReadOnlyList<StepRun> StepRuns,
    // Newest first (AC5 of #14). Not a trailing optional on purpose: #13 learned that a projection one
    // may forget to pass still compiles - and would then report an empty history for every submission.
    IReadOnlyList<SubmissionHistoryEntry> History, string Handling, SubmissionState State,
    string? BrevoContactId, bool CanResendDoi, IReadOnlyList<QuizAnswerView>? QuizAnswers = null,
    string? Assignee = null,
    // #15: see SubmissionListItem for what ExpiresAt/RetainedIndefinitely mean.
    DateTimeOffset ExpiresAt = default, bool RetainedIndefinitely = false, bool HasRetentionOverride = false);

public sealed record QuizAnswerView(string Question, string Answer, int Points, bool Jumped);

// Assigning a submission to an admin (#13). Assign, hand over and unassign are one operation:
// the submission carries at most one assignee, and setting it to null is how it loses one.

public interface ISetSubmissionAssigneeUseCase
{
    /// <summary>Hands one submission to an admin, or to nobody (null). Notifies no one, ever.</summary>
    /// <param name="by">The signed-in admin, null when the request carried no identity (#14).</param>
    Task ExecuteAsync(string submissionId, string? assignee, string? by, CancellationToken ct = default);
}

public interface IListAdminsUseCase
{
    /// <summary>The admins the application has seen at least once - the choices an assignment has.</summary>
    Task<IReadOnlyList<string>> ExecuteAsync(CancellationToken ct = default);
}

public interface IRecordAdminSeenUseCase
{
    /// <summary>Notes that this admin has been here - without touching their last visit.</summary>
    Task ExecuteAsync(string user, CancellationToken ct = default);
}

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
    /// <param name="by">The signed-in admin, null when the request carried no identity (#14).</param>
    Task ExecuteAsync(string submissionId, string handling, string? by, CancellationToken ct = default);   // open | done
}

/// <summary>The three ways an admin can change a submission's retention (#15). No date field to type -
/// the issue is explicit that the occasion to extend almost never has a known end date.</summary>
public enum SubmissionRetentionAction { RetainPermanently, Extend, Lift }

public interface ISetSubmissionRetentionUseCase
{
    /// <param name="by">The signed-in admin, null when the request carried no identity (#14).</param>
    Task ExecuteAsync(string submissionId, SubmissionRetentionAction action, string? by, CancellationToken ct = default);
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
    /// <param name="by">The signed-in admin, null when the request carried no identity (#14).</param>
    Task ExecuteAsync(string submissionId, string stepId, string? by, CancellationToken ct = default);
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

// Appointments of a form (#6): create, list, deactivate, delete. Master data of a slug.

public interface IListAppointmentsUseCase
{
    /// <summary>Every appointment of a form for the admin - active and inactive, newest start first.</summary>
    Task<IReadOnlyList<Appointment>> ExecuteAsync(string slug, CancellationToken ct = default);
}

public interface ISaveAppointmentUseCase
{
    /// <summary>Creates (empty id → a new one is assigned) or updates an appointment, and returns the stored
    /// value. Rejects an end that is not after its start (AC 6).</summary>
    Task<Appointment> ExecuteAsync(Appointment appointment, CancellationToken ct = default);
}

public interface IDeactivateAppointmentUseCase
{
    /// <summary>Marks the appointment inactive; it stays in the admin and keeps its identity. Unknown id → not found.</summary>
    Task ExecuteAsync(string slug, string id, CancellationToken ct = default);
}

public interface IDeleteAppointmentUseCase
{
    /// <summary>Removes the appointment. The caller is responsible for the explicit confirmation (AC 5).</summary>
    Task ExecuteAsync(string slug, string id, CancellationToken ct = default);
}
