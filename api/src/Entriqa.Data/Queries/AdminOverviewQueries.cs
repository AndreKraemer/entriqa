using Azure;
using Entriqa.Application.Ports;
using Entriqa.Data.Entities;
using Entriqa.Data.Mapping;
using Entriqa.Domain.Submissions;

namespace Entriqa.Data.Queries;

internal sealed class ListRecentSubmissionsQuery(TableStorage storage) : IListRecentSubmissionsQuery
{
    public async Task<IReadOnlyList<SubmissionListItem>> ExecuteAsync(string? slug, int max, CancellationToken ct = default)
    {
        var table = await storage.GetAsync("Submissions");
        var result = new List<SubmissionListItem>();
        // Je Partition sind die RowKeys bereits neueste-zuerst (invertierte Ticks); über alle Partitionen
        // wird nach dem Scan sortiert – bei diesem Volumen (Formulare zweier Firmenwebsites) unkritisch.
        var query = slug is null
            ? table.QueryAsync<SubmissionEntity>(cancellationToken: ct)
            : table.QueryAsync<SubmissionEntity>(e => e.PartitionKey == slug, cancellationToken: ct);
        await foreach (var e in query)
        {
            result.Add(SubmissionMapper.ToListItem(e));
            if (result.Count >= 5000) break;                     // Notbremse, weit über realem Volumen
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
        return e.HasValue ? (e.Value!.LastVisitAt, e.Value!.Note ?? "") : null;
    }
}

internal sealed class GetLastVisitQuery(TableStorage storage) : IGetLastVisitQuery
{
    public async Task<DateTimeOffset?> ExecuteAsync(string user, CancellationToken ct = default)
    {
        var table = await storage.GetAsync("AdminState");
        var e = await table.GetEntityIfExistsAsync<AdminStateEntity>("state", user, cancellationToken: ct);
        return e.HasValue ? e.Value!.LastVisitAt : null;
    }
}
