using Entriqa.Domain.Forms;
using Entriqa.Domain.Submissions;

namespace Entriqa.Application.Ports;

// Read and write ports of the data layer. No repositories: one port = one intent (Solution Standard §13/§14).

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
    /// <summary>Creates the submission. Atomic: one upsert, one entity.</summary>
    Task ExecuteAsync(Submission submission, CancellationToken ct = default);
}

public interface ISaveSubmissionCommand
{
    /// <summary>Writes step status, artifacts and confirmation back. Atomic as above.</summary>
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
    /// <summary>true the first time, false when the nonce was already used (replay).</summary>
    Task<bool> ExecuteAsync(string nonce, DateTimeOffset expiresAt, CancellationToken ct = default);
}

public interface IRegisterRateLimitHitCommand
{
    /// <summary>Counts a hit for (ipHash, time window) and returns the new count.</summary>
    Task<int> ExecuteAsync(string ipHash, DateTimeOffset windowStart, CancellationToken ct = default);
}

// Funnel counters (drop-off analytics without personal data: daily totals per form only)

public interface IIncrementFunnelCommand
{
    Task ExecuteAsync(string slug, DateOnly day, string type, CancellationToken ct = default);
}

public interface IGetFunnelTotalsQuery
{
    /// <summary>Totals per event type from <paramref name="from"/> onwards (inclusive).</summary>
    Task<IReadOnlyDictionary<string, int>> ExecuteAsync(string slug, DateOnly from, CancellationToken ct = default);
}

// Contact view (a scan across the submissions - small volume, bounded by the retention period)

public interface IListContactSubmissionsQuery
{
    /// <summary>Every submission WITH an email address (complete, for aggregating per contact and company).</summary>
    Task<IReadOnlyList<Submission>> ListWithEmailAsync(int max, CancellationToken ct = default);
    /// <summary>Every submission of one address (case-insensitive), newest first.</summary>
    Task<IReadOnlyList<SubmissionListItem>> ListByEmailAsync(string email, CancellationToken ct = default);
}

// Admin overview

public interface IListRecentSubmissionsQuery
{
    /// <summary>Latest submissions, optionally limited to one form; sorted by arrival, descending.</summary>
    Task<IReadOnlyList<SubmissionListItem>> ExecuteAsync(string? slug, int max, CancellationToken ct = default);
}

public interface IListSubmissionsForStatsQuery
{
    /// <summary>Every submission of a form as domain objects - for statistics and CSV export.</summary>
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
    /// <summary>Unfinished submissions (processing/failed) older than the grace period - for sweep and auto retry.</summary>
    Task<IReadOnlyList<Submission>> ListUnfinishedAsync(DateTimeOffset olderThan, int max, CancellationToken ct = default);

    /// <summary>Expired submissions: generally older than <paramref name="generalCutoff"/>, unconfirmed DOI older than <paramref name="unconfirmedCutoff"/>.</summary>
    Task<IReadOnlyList<Submission>> ListExpiredAsync(DateTimeOffset generalCutoff, DateTimeOffset unconfirmedCutoff, int max, CancellationToken ct = default);
}

public interface IDeleteSubmissionCommand
{
    Task ExecuteAsync(Submission submission, CancellationToken ct = default);
}

public interface IPurgeSecurityEntriesCommand
{
    /// <summary>Deletes expired nonces and old rate-limit windows. Returns the count per table.</summary>
    Task<(int Nonces, int RateLimits)> ExecuteAsync(DateTimeOffset now, CancellationToken ct = default);
}

// Admin ports (draft and version) - for the Blazor admin.

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
    /// <summary>Creates the Forms entry or updates the draft; the status stays untouched (new = draft).</summary>
    Task ExecuteAsync(FormDefinition definition, string savedBy, CancellationToken ct = default);
}
