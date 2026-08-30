using Entriqa.Application.Ports;
using Entriqa.Data.Entities;
using Entriqa.Data.Mapping;
using Entriqa.Domain.Consent;

namespace Entriqa.Data.Queries;

/// <summary>
/// Only ever asked when ConsentRetentionDays is set (#1) - at the default of 0 nothing calls this,
/// and a filter scan is the right shape for a query that runs at most once every housekeeping pass.
/// </summary>
internal sealed class ListExpiredConsentProofsQuery(TableStorage storage) : IListExpiredConsentProofsQuery
{
    public async Task<IReadOnlyList<ConsentProof>> ExecuteAsync(DateTimeOffset olderThan, int max, CancellationToken ct = default)
    {
        var table = await storage.GetAsync("ConsentProofs");
        var result = new List<ConsentProof>();
        await foreach (var e in table.QueryAsync<ConsentProofEntity>(e => e.SubmittedAt < olderThan, cancellationToken: ct))
        {
            result.Add(ConsentProofMapper.ToDomain(e));
            if (result.Count >= max) break;
        }
        return result;
    }
}
