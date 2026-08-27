using System.Globalization;
using Azure;
using Azure.Data.Tables;
using Entriqa.Application.Ports;
using Entriqa.Data.Entities;

namespace Entriqa.Data.Commands;

internal sealed class TryConsumeNonceCommand(TableStorage storage) : ITryConsumeNonceCommand
{
    public async Task<bool> ExecuteAsync(string nonce, DateTimeOffset expiresAt, CancellationToken ct = default)
    {
        var table = await storage.GetAsync("Nonces");
        try
        {
            await table.AddEntityAsync(new NonceEntity { RowKey = nonce, ExpiresAt = expiresAt }, ct);   // Add fails on a duplicate -> atomic first use
            return true;
        }
        catch (RequestFailedException ex) when (ex.Status == 409) { return false; }
    }
}

internal sealed class RegisterRateLimitHitCommand(TableStorage storage) : IRegisterRateLimitHitCommand
{
    public async Task<int> ExecuteAsync(string ipHash, DateTimeOffset windowStart, CancellationToken ct = default)
    {
        var table = await storage.GetAsync("RateLimits");
        var rowKey = windowStart.UtcTicks.ToString(CultureInfo.InvariantCulture);
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                var existing = await table.GetEntityIfExistsAsync<RateLimitEntity>(ipHash, rowKey, cancellationToken: ct);
                if (!existing.HasValue)
                {
                    await table.AddEntityAsync(new RateLimitEntity { PartitionKey = ipHash, RowKey = rowKey, Count = 1 }, ct);
                    return 1;
                }
                var e = existing.Value!;
                e.Count++;
                await table.UpdateEntityAsync(e, e.ETag, TableUpdateMode.Replace, ct);   // optimistic: an ETag conflict -> try again
                return e.Count;
            }
            catch (RequestFailedException ex) when (ex.Status is 409 or 412) { /* Wettlauf, nochmal */ }
        }
        return int.MaxValue;                                                 // im Zweifel ablehnen
    }
}
