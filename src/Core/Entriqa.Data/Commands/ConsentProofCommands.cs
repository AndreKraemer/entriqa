using Azure;
using Azure.Data.Tables;
using Entriqa.Application.Ports;
using Entriqa.Data.Entities;
using Entriqa.Data.Mapping;
using Entriqa.Domain.Consent;

namespace Entriqa.Data.Commands;

// The consent proof lives in its own table with its own clock (#1). No ETag guard anywhere here:
// a proof is written once and only ever amended by its own confirmation - there is no second writer
// to lose a race against.

internal sealed class StoreConsentProofCommand(TableStorage storage) : IStoreConsentProofCommand
{
    public async Task ExecuteAsync(ConsentProof proof, CancellationToken ct = default)
    {
        var table = await storage.GetAsync("ConsentProofs");
        await table.UpsertEntityAsync(ConsentProofMapper.ToEntity(proof), TableUpdateMode.Replace, ct);
    }
}

internal sealed class ConfirmConsentProofCommand(TableStorage storage) : IConfirmConsentProofCommand
{
    public async Task ExecuteAsync(string submissionId, DateTimeOffset confirmedAt, string? confirmedIpHash,
                                   CancellationToken ct = default)
    {
        // The confirmation knows the submission, not the partition it was filed under, so this is a
        // scan on the row key - the same trade the contact and housekeeping queries make at this volume.
        var table = await storage.GetAsync("ConsentProofs");
        await foreach (var e in table.QueryAsync<ConsentProofEntity>(e => e.RowKey == submissionId, cancellationToken: ct))
        {
            e.ConfirmedAt = confirmedAt;
            e.ConfirmedIpHash = confirmedIpHash;
            await table.UpdateEntityAsync(e, ETag.All, TableUpdateMode.Replace, ct);
        }
    }
}

internal sealed class DeleteConsentProofsByEmailCommand(TableStorage storage) : IDeleteConsentProofsByEmailCommand
{
    public async Task<int> ExecuteAsync(string email, CancellationToken ct = default)
    {
        var table = await storage.GetAsync("ConsentProofs");
        var partition = ConsentProofMapper.PartitionOf(email);
        var deleted = 0;
        await foreach (var e in table.QueryAsync<ConsentProofEntity>(e => e.PartitionKey == partition, cancellationToken: ct))
        {
            await table.DeleteEntityAsync(e.PartitionKey, e.RowKey, ETag.All, ct);      // 404 does not throw - idempotent
            deleted++;
        }
        return deleted;
    }
}

internal sealed class DeleteConsentProofCommand(TableStorage storage) : IDeleteConsentProofCommand
{
    public async Task ExecuteAsync(ConsentProof proof, CancellationToken ct = default)
    {
        var table = await storage.GetAsync("ConsentProofs");
        await table.DeleteEntityAsync(ConsentProofMapper.PartitionOf(proof.Email), proof.SubmissionId, ETag.All, ct);
    }
}

internal sealed class RecordConsentDeletionCommand(TableStorage storage) : IRecordConsentDeletionCommand
{
    public async Task ExecuteAsync(DateTimeOffset at, string emailHash, int count, string by, string? submissionId,
                                   CancellationToken ct = default)
    {
        // Structured, not a sentence: the erasure of an address must not be documented with that address,
        // and a machine-readable row survives the language the admin will later render it in.
        var table = await storage.GetAsync("AdminState");
        await table.UpsertEntityAsync(new AdminStateEntity
        {
            PartitionKey = "consentdeletion",
            // Newest first. The submission id joins the key because #2 made one row per erased proof
            // the normal case: without it two erasures of the same address collapse onto one row, and
            // the id the row carries to say which proof went is exactly what would be lost.
            RowKey = $"{DateTimeOffset.MaxValue.Ticks - at.Ticks:D19}-{emailHash}" +
                     (submissionId is { Length: > 0 } id ? $"-{id}" : ""),
            LastVisitAt = at,
            Note = emailHash,
            Count = count,
            By = by,
            SubmissionId = submissionId,
        }, TableUpdateMode.Replace, ct);
    }
}
