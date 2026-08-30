using Entriqa.Application.Ports;
using Entriqa.Domain.Consent;

namespace Entriqa.Data.Queries;

// SKELETON (#1) - see ConsentProofCommands.

internal sealed class ListExpiredConsentProofsQuery : IListExpiredConsentProofsQuery
{
    public Task<IReadOnlyList<ConsentProof>> ExecuteAsync(DateTimeOffset olderThan, int max, CancellationToken ct = default)
        => throw new NotImplementedException();
}
