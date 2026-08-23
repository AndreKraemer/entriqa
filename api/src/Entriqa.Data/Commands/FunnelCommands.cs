using Azure;
using Azure.Data.Tables;
using Entriqa.Application.Ports;
using Entriqa.Data.Entities;

namespace Entriqa.Data.Commands;

/// <summary>
/// Tageszähler je (Formular, Ereignis) – aggregiert, ohne jeden Personenbezug (DSGVO: kein Cookie,
/// keine ID, kein Inhalt). Optimistisches Inkrement wie beim Rate-Limit; ein verlorener Zähltreffer
/// unter Last ist für eine Trend-Statistik verschmerzbar.
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
    public async Task<IReadOnlyDictionary<string, int>> ExecuteAsync(string slug, DateOnly from, CancellationToken ct = default)
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
