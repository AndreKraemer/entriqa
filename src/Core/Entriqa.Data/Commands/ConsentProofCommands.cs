using Entriqa.Application.Ports;
using Entriqa.Domain.Consent;

namespace Entriqa.Data.Commands;

// SKELETON (#1): the table and the entity exist, the access does not. The red tests substitute
// these ports, so nothing here is executed yet.

internal sealed class StoreConsentProofCommand : IStoreConsentProofCommand
{
    public Task ExecuteAsync(ConsentProof proof, CancellationToken ct = default) => throw new NotImplementedException();
}

internal sealed class ConfirmConsentProofCommand : IConfirmConsentProofCommand
{
    public Task ExecuteAsync(string submissionId, DateTimeOffset confirmedAt, string? confirmedIpHash, CancellationToken ct = default)
        => throw new NotImplementedException();
}

internal sealed class DeleteConsentProofsByEmailCommand : IDeleteConsentProofsByEmailCommand
{
    public Task<int> ExecuteAsync(string email, CancellationToken ct = default) => throw new NotImplementedException();
}

internal sealed class DeleteConsentProofCommand : IDeleteConsentProofCommand
{
    public Task ExecuteAsync(ConsentProof proof, CancellationToken ct = default) => throw new NotImplementedException();
}

internal sealed class RecordConsentDeletionCommand : IRecordConsentDeletionCommand
{
    public Task ExecuteAsync(DateTimeOffset at, string emailHash, int count, CancellationToken ct = default)
        => throw new NotImplementedException();
}
