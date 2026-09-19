using Azure;
using Microsoft.Extensions.Options;
using Entriqa.Application;
using Entriqa.Application.Ports;
using Entriqa.Data.Entities;
using Entriqa.Data.Mapping;
using Entriqa.Domain.Submissions;

namespace Entriqa.Data.Queries;

internal sealed class ListRecentSubmissionsQuery(TableStorage storage, IOptions<EntriqaOptions> options) : IListRecentSubmissionsQuery
{
    public async Task<IReadOnlyList<SubmissionListItem>> ExecuteAsync(string? slug, int max, CancellationToken ct = default)
    {
        var table = await storage.GetAsync("Submissions");
        var result = new List<SubmissionListItem>();
        // Within a partition the row keys are newest-first already (inverted ticks); across all partitions
        // sorting happens after the scan - uncritical at this volume (the forms of two company websites).
        var query = slug is null
            ? table.QueryAsync<SubmissionEntity>(cancellationToken: ct)
            : table.QueryAsync<SubmissionEntity>(e => e.PartitionKey == slug, cancellationToken: ct);
        var retentionDays = options.Value.RetentionDays;
        await foreach (var e in query)
        {
            result.Add(SubmissionRetention.Project(SubmissionMapper.ToListItem(e), e.RetainUntil, retentionDays));
            if (result.Count >= 5000) break;                     // emergency brake, far above the real volume
        }
        return result.OrderByDescending(s => s.CreatedAt).Take(max).ToList();
    }
}

internal sealed class ListSubmissionsForStatsQuery(TableStorage storage) : IListSubmissionsForStatsQuery
{
    public async Task<IReadOnlyList<Submission>> ExecuteAsync(string slug, int max, CancellationToken ct = default)
    {
        var table = await storage.GetAsync("Submissions");
        var result = new List<Submission>();
        await foreach (var e in table.QueryAsync<SubmissionEntity>(e => e.PartitionKey == slug, cancellationToken: ct))
        {
            result.Add(SubmissionMapper.ToDomain(e));
            if (result.Count >= max) break;
        }
        return result;
    }
}

internal sealed class ListAllSubmissionsForStatsQuery(TableStorage storage) : IListAllSubmissionsForStatsQuery
{
    public async Task<IReadOnlyList<Submission>> ExecuteAsync(int max, CancellationToken ct = default)
    {
        var table = await storage.GetAsync("Submissions");
        var result = new List<Submission>();
        // Full-table scan across every form's partition - established at this volume (see ContactQueries).
        await foreach (var e in table.QueryAsync<SubmissionEntity>(maxPerPage: 1000, cancellationToken: ct))
        {
            result.Add(SubmissionMapper.ToDomain(e));
            if (result.Count >= max) break;
        }
        return result;
    }
}

internal sealed class GetFormsActivityQuery(TableStorage storage) : IGetFormsActivityQuery
{
    public async Task<IReadOnlyDictionary<string, Domain.UseCases.FormActivity>> ExecuteAsync(DateTimeOffset today, CancellationToken ct = default)
    {
        var table = await storage.GetAsync("Submissions");
        var totals = new Dictionary<string, int>();
        var daily = new Dictionary<string, int[]>();
        await foreach (var e in table.QueryAsync<SubmissionEntity>(cancellationToken: ct))
        {
            totals[e.PartitionKey] = totals.GetValueOrDefault(e.PartitionKey) + 1;
            var idx = 13 - (int)(today.UtcDateTime.Date - e.CreatedAt.UtcDateTime.Date).TotalDays;
            if (idx is >= 0 and <= 13)
                (daily.TryGetValue(e.PartitionKey, out var d) ? d : daily[e.PartitionKey] = new int[14])[idx]++;
        }
        return totals.ToDictionary(kv => kv.Key,
            kv => new Domain.UseCases.FormActivity(kv.Value, daily.GetValueOrDefault(kv.Key) ?? new int[14]));
    }
}

internal sealed class GetHousekeepingRunQuery(TableStorage storage) : IGetHousekeepingRunQuery
{
    public async Task<(DateTimeOffset At, string Summary)?> ExecuteAsync(CancellationToken ct = default)
    {
        var table = await storage.GetAsync("AdminState");
        var e = await table.GetEntityIfExistsAsync<AdminStateEntity>("housekeeping", "last", cancellationToken: ct);
        return e.HasValue && e.Value!.LastVisitAt is { } at ? (at, e.Value!.Note ?? "") : null;
    }
}

internal sealed class GetLastVisitQuery(TableStorage storage) : IGetLastVisitQuery
{
    public async Task<DateTimeOffset?> ExecuteAsync(string user, CancellationToken ct = default)
    {
        var table = await storage.GetAsync("AdminState");
        var e = await table.GetEntityIfExistsAsync<AdminStateEntity>("state", user, cancellationToken: ct);
        return e.HasValue ? e.Value!.LastVisitAt : null;        // null for a row that only records a sighting (#13)
    }
}

/// <summary>
/// The admins the application has seen, which is what an assignment can choose from (#13). The row
/// keys of the state partition are those admins: one is written when someone marks everything as
/// seen, and since #13 also when they merely open the inbox. Someone who has never been in the
/// admin is not offered - Entriqa keeps no user management, and the people invited in the SWA role
/// administration are unknown to it.
/// </summary>
internal sealed class ListAdminsQuery(TableStorage storage) : IListAdminsQuery
{
    public async Task<IReadOnlyList<string>> ExecuteAsync(CancellationToken ct = default)
    {
        var table = await storage.GetAsync("AdminState");
        var admins = new List<string>();
        await foreach (var e in table.QueryAsync<AdminStateEntity>(e => e.PartitionKey == "state", cancellationToken: ct))
            admins.Add(e.RowKey);
        admins.Sort(StringComparer.Ordinal);                    // the picker's order must not depend on the scan
        return admins;
    }
}
