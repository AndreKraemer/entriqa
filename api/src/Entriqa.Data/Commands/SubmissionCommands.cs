using Azure;
using Azure.Data.Tables;
using Entriqa.Application.Ports;
using Entriqa.Data.Mapping;
using Entriqa.Domain.Errors;
using Entriqa.Domain.Submissions;

namespace Entriqa.Data.Commands;

// One command = one atomic upsert. The submission is one entity, everything else lives inside it as JSON.
// Updates are ETag guarded: confirm, deferred run and admin retry can work on the same submission
// at the same time (human plus mail scanner) - the loser gets a conflict instead of last writer wins.

internal sealed class StoreSubmissionCommand(TableStorage storage) : IStoreSubmissionCommand
{
    public async Task ExecuteAsync(Submission submission, CancellationToken ct = default)
    {
        var table = await storage.GetAsync("Submissions");
        var response = await table.AddEntityAsync(SubmissionMapper.ToEntity(submission), ct);
        submission.ETag = response.Headers.ETag?.ToString();
    }
}

internal sealed class SaveSubmissionCommand(TableStorage storage) : ISaveSubmissionCommand
{
    public async Task ExecuteAsync(Submission submission, CancellationToken ct = default)
    {
        var table = await storage.GetAsync("Submissions");
        var entity = SubmissionMapper.ToEntity(submission);
        try
        {
            var response = submission.ETag is { } etag
                ? await table.UpdateEntityAsync(entity, new ETag(etag), TableUpdateMode.Replace, ct)
                : await table.UpsertEntityAsync(entity, TableUpdateMode.Replace, ct); // tests and seed only, without loading first
            submission.ETag = response.Headers.ETag?.ToString();
        }
        catch (RequestFailedException ex) when (ex.Status == 412)
        {
            throw new AppException(ErrorCodes.Conflict, "Die Einsendung wurde parallel verändert.", 409, ex);
        }
    }
}
