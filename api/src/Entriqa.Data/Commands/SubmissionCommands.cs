using Azure;
using Azure.Data.Tables;
using Entriqa.Application.Ports;
using Entriqa.Data.Mapping;
using Entriqa.Domain.Errors;
using Entriqa.Domain.Submissions;

namespace Entriqa.Data.Commands;

// Ein Command = ein atomarer Upsert. Die Submission ist ein Entity, alles Weitere liegt als JSON darin.
// Updates laufen ETag-geschützt: Confirm, Deferred-Lauf und Admin-Retry können gleichzeitig auf derselben
// Einsendung arbeiten (Mensch + Mail-Scanner) – der Verlierer bekommt einen Conflict statt Last-Writer-Wins.

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
                : await table.UpsertEntityAsync(entity, TableUpdateMode.Replace, ct); // nur Tests/Seed ohne vorheriges Laden
            submission.ETag = response.Headers.ETag?.ToString();
        }
        catch (RequestFailedException ex) when (ex.Status == 412)
        {
            throw new AppException(ErrorCodes.Conflict, "Die Einsendung wurde parallel verändert.", 409, ex);
        }
    }
}
