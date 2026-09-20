using Azure;
using Azure.Data.Tables;
using Entriqa.Application.Ports;
using Entriqa.Data.Entities;
using Entriqa.Domain.UseCases;

namespace Entriqa.Data.Commands;

/// <summary>
/// Daily counters per (form, event) - aggregated, without any personal reference (GDPR: no cookie,
/// no id, no content). Optimistic increment as with the rate limit; a counting hit lost
/// under load is bearable for a trend statistic.
/// </summary>
internal sealed class IncrementFunnelCommand(TableStorage storage) : IIncrementFunnelCommand
{
    public async Task ExecuteAsync(string slug, DateOnly day, string type, CancellationToken ct = default)
    {
        var table = await storage.GetAsync("Funnel");
        var rowKey = $"{day:yyyyMMdd}|{type}";
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                var existing = await table.GetEntityIfExistsAsync<FunnelEntity>(slug, rowKey, cancellationToken: ct);
                if (!existing.HasValue)
                {
                    await table.AddEntityAsync(new FunnelEntity { PartitionKey = slug, RowKey = rowKey, Count = 1 }, ct);
                    return;
                }
                var e = existing.Value!;
                e.Count++;
                await table.UpdateEntityAsync(e, e.ETag, TableUpdateMode.Replace, ct);
                return;
            }
            catch (RequestFailedException ex) when (ex.Status is 409 or 412) { /* Wettlauf, nochmal */ }
        }
    }
}

internal sealed class GetFunnelTotalsQuery(TableStorage storage) : IGetFunnelTotalsQuery
{
    public async Task<IReadOnlyDictionary<string, int>> ExecuteAsync(string slug, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var table = await storage.GetAsync("Funnel");
        var fromKey = $"{from:yyyyMMdd}|";
        var totals = new Dictionary<string, int>();
        await foreach (var e in table.QueryAsync<FunnelEntity>(x => x.PartitionKey == slug && x.RowKey.CompareTo(fromKey) >= 0, cancellationToken: ct))
        {
            var type = e.RowKey.Split('|') is { Length: 2 } parts ? parts[1] : "?";
            totals[type] = totals.GetValueOrDefault(type) + e.Count;
        }
        return totals;
    }
}

/// <summary>
/// View and start totals per form from <paramref name="from"/> onwards, across all forms (#16). A
/// full-table scan of the tiny Funnel table (two rows per form and day) - uncritical at this volume.
/// </summary>
internal sealed class GetAllFunnelTotalsQuery(TableStorage storage) : IGetAllFunnelTotalsQuery
{
    public async Task<IReadOnlyDictionary<string, FunnelStats>> ExecuteAsync(DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var table = await storage.GetAsync("Funnel");
        var fromKey = $"{from:yyyyMMdd}|";
        var views = new Dictionary<string, int>(StringComparer.Ordinal);
        var starts = new Dictionary<string, int>(StringComparer.Ordinal);
        await foreach (var e in table.QueryAsync<FunnelEntity>(x => x.RowKey.CompareTo(fromKey) >= 0, cancellationToken: ct))
        {
            var bucket = (e.RowKey.Split('|') is { Length: 2 } parts ? parts[1] : "?") switch
            {
                "view" => views,
                "start" => starts,
                _ => null,
            };
            if (bucket is not null) bucket[e.PartitionKey] = bucket.GetValueOrDefault(e.PartitionKey) + e.Count;
        }
        return views.Keys.Union(starts.Keys, StringComparer.Ordinal)
            .ToDictionary(slug => slug, slug => new FunnelStats(views.GetValueOrDefault(slug), starts.GetValueOrDefault(slug)), StringComparer.Ordinal);
    }
}
