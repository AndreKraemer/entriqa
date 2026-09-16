using Entriqa.Application.Ports;
using Entriqa.Domain.UseCases;

namespace Entriqa.Application.UseCases;

internal sealed class ListRecentSubmissionsUseCase(
    IListRecentSubmissionsQuery list,
    IGetLastVisitQuery lastVisit) : IListRecentSubmissionsUseCase
{
    public async Task<RecentSubmissions> ExecuteAsync(string? slug, string user, int max = 500, CancellationToken ct = default)
    {
        var items = await list.ExecuteAsync(slug, Math.Clamp(max, 1, 1000), ct);
        return new RecentSubmissions(items, await lastVisit.ExecuteAsync(user, ct));
    }
}

/// <summary>
/// The search behind the inbox (#12). It owns the whole match - trimming, case, which candidate texts
/// count and the order of the hits - because this is the layer the unit tests execute; the query below
/// it only reads rows.
/// </summary>
internal sealed class SearchSubmissionsUseCase(
    ISearchSubmissionsQuery search,
    IGetLastVisitQuery lastVisit) : ISearchSubmissionsUseCase
{
    public Task<SubmissionSearchResult> ExecuteAsync(string? slug, string term, string user, int scanMax = 5000, CancellationToken ct = default)
    {
        _ = search;
        _ = lastVisit;
        throw new NotImplementedException();
    }
}

internal sealed class GetFormsActivityUseCase(IGetFormsActivityQuery query, TimeProvider time) : IGetFormsActivityUseCase
{
    public async Task<IReadOnlyDictionary<string, FormActivity>> ExecuteAsync(CancellationToken ct = default)
        => await query.ExecuteAsync(time.GetUtcNow(), ct);
}

internal sealed class GetAdminStatusUseCase(
    IBrevoDirectoryPort brevo,
    IGetHousekeepingRunQuery housekeepingRun,
    Security.LicenseService license,
    Microsoft.Extensions.Options.IOptions<EntriqaOptions> options) : IGetAdminStatusUseCase
{
    public async Task<AdminStatus> ExecuteAsync(CancellationToken ct = default)
    {
        var o = options.Value;
        var brevoOk = false;
        if (!string.IsNullOrEmpty(o.Brevo.ApiKey))
        {
            try { brevoOk = await brevo.IsReachableAsync(ct); }
            catch (Exception ex) when (ex is not OperationCanceledException) { brevoOk = false; }
        }
        var run = await housekeepingRun.ExecuteAsync(ct);
        return new AdminStatus(o.SiteName, o.BaseUrl,
            !string.IsNullOrEmpty(o.Brevo.ApiKey), brevoOk,
            !string.IsNullOrEmpty(o.ReportingCloud.ApiKey),
            o.RetentionDays, o.UnconfirmedRetentionDays, o.SweepAfterMinutes, o.AutoRetryMax,
            run?.At, run?.Summary, o.SiteLocales,
            license.Check() is var l ? new LicenseView(l.Valid, l.Status, l.Plan, l.ValidUntil) : null!);
    }
}

internal sealed class MarkVisitedUseCase(ISetLastVisitCommand set, TimeProvider time) : IMarkVisitedUseCase
{
    public async Task<DateTimeOffset> ExecuteAsync(string user, CancellationToken ct = default)
    {
        var now = time.GetUtcNow();
        await set.ExecuteAsync(user, now, ct);
        return now;
    }
}

/// <summary>The choices an assignment has (#13) - the admins the application has seen.</summary>
internal sealed class ListAdminsUseCase(IListAdminsQuery admins) : IListAdminsUseCase
{
    public Task<IReadOnlyList<string>> ExecuteAsync(CancellationToken ct = default) => admins.ExecuteAsync(ct);
}

/// <summary>
/// What turns "has been in the admin" into a row the assignment picker can read (#13). Called from
/// the inbox endpoint, which every admin passes on every page load - recording it where the
/// assignment happens would mean only people who have already assigned something can be assigned
/// something.
/// </summary>
internal sealed class RecordAdminSeenUseCase(IRecordAdminSeenCommand record, TimeProvider time) : IRecordAdminSeenUseCase
{
    public Task ExecuteAsync(string user, CancellationToken ct = default) =>
        record.ExecuteAsync(user, time.GetUtcNow(), ct);
}
