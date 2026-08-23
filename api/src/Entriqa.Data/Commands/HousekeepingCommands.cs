using Azure;
using Entriqa.Application.Ports;
using Entriqa.Data.Entities;
using Entriqa.Domain.Submissions;

namespace Entriqa.Data.Commands;

internal sealed class DeleteSubmissionCommand(TableStorage storage) : IDeleteSubmissionCommand
{
    public async Task ExecuteAsync(Submission submission, CancellationToken ct = default)
    {
        var sep = submission.Id.IndexOf(':');
        var table = await storage.GetAsync("Submissions");
        await table.DeleteEntityAsync(submission.Id[..sep], submission.Id[(sep + 1)..], ETag.All, ct);   // 404 wirft nicht – idempotent
    }
}

internal sealed class PurgeSecurityEntriesCommand(TableStorage storage) : IPurgeSecurityEntriesCommand
{
    public async Task<(int Nonces, int RateLimits)> ExecuteAsync(DateTimeOffset now, CancellationToken ct = default)
    {
        var nonces = await storage.GetAsync("Nonces");
        var purgedNonces = 0;
        await foreach (var e in nonces.QueryAsync<NonceEntity>(e => e.ExpiresAt < now, cancellationToken: ct))
        {
            await nonces.DeleteEntityAsync(e.PartitionKey, e.RowKey, ETag.All, ct);
            purgedNonces++;
        }

        var limits = await storage.GetAsync("RateLimits");
        var cutoff = now.AddDays(-1);                                        // Fenster sind 10 min – nach einem Tag sicher irrelevant
        var purgedLimits = 0;
        await foreach (var e in limits.QueryAsync<RateLimitEntity>(e => e.Timestamp < cutoff, cancellationToken: ct))
        {
            await limits.DeleteEntityAsync(e.PartitionKey, e.RowKey, ETag.All, ct);
            purgedLimits++;
        }
        return (purgedNonces, purgedLimits);
    }
}
