using Azure;
using Entriqa.Application.Ports;
using Entriqa.Data.Entities;
using Entriqa.Data.Mapping;
using Entriqa.Domain.Submissions;

namespace Entriqa.Data.Queries;

internal sealed class TryGetSubmissionQuery(TableStorage storage) : ITryGetSubmissionQuery
{
    public async Task<Submission?> ExecuteAsync(string submissionId, CancellationToken ct = default)
    {
        var sep = submissionId.IndexOf(':');
        if (sep <= 0) return null;
        var table = await storage.GetAsync("Submissions");
        try
        {
            var e = (await table.GetEntityAsync<SubmissionEntity>(submissionId[..sep], submissionId[(sep + 1)..], cancellationToken: ct)).Value;
            return SubmissionMapper.ToDomain(e);
        }
        catch (RequestFailedException ex) when (ex.Status == 404) { return null; }
    }
}

internal sealed class ListSubmissionsQuery(TableStorage storage) : IListSubmissionsQuery
{
    public async Task<SubmissionPage> ExecuteAsync(string slug, string? continuationToken, int pageSize, CancellationToken ct = default)
    {
        var table = await storage.GetAsync("Submissions");
        var pages = table.QueryAsync<SubmissionEntity>(e => e.PartitionKey == slug, maxPerPage: pageSize, cancellationToken: ct)
            .AsPages(continuationToken, pageSize);
        await foreach (var page in pages)
            return new SubmissionPage(page.Values.Select(SubmissionMapper.ToListItem).ToList(), page.ContinuationToken);
        return new SubmissionPage(Array.Empty<SubmissionListItem>(), null);
    }
}
