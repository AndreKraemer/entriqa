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

/// <summary>#13 skeleton - the choices an assignment has.</summary>
internal sealed class ListAdminsUseCase(IListAdminsQuery admins) : IListAdminsUseCase
{
    public Task<IReadOnlyList<string>> ExecuteAsync(CancellationToken ct = default)
    {
        _ = admins; _ = ct;
        throw new NotImplementedException("#13");
    }
}

/// <summary>#13 skeleton - what turns "has signed in" into a row the assignment list can read.</summary>
internal sealed class RecordAdminSeenUseCase(IRecordAdminSeenCommand record, TimeProvider time) : IRecordAdminSeenUseCase
{
    public Task ExecuteAsync(string user, CancellationToken ct = default)
    {
        _ = record; _ = time; _ = user; _ = ct;
        throw new NotImplementedException("#13");
    }
}
