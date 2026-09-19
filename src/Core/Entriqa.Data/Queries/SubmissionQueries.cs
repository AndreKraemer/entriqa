using Azure;
using Microsoft.Extensions.Options;
using Entriqa.Application;
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

internal sealed class ListSubmissionsQuery(TableStorage storage, IOptions<EntriqaOptions> options) : IListSubmissionsQuery
{
    public async Task<SubmissionPage> ExecuteAsync(string slug, string? continuationToken, int pageSize, CancellationToken ct = default)
    {
        var table = await storage.GetAsync("Submissions");
        var retentionDays = options.Value.RetentionDays;
        var pages = table.QueryAsync<SubmissionEntity>(e => e.PartitionKey == slug, maxPerPage: pageSize, cancellationToken: ct)
            .AsPages(continuationToken, pageSize);
        await foreach (var page in pages)
            return new SubmissionPage(
                page.Values.Select(e => SubmissionRetention.Project(SubmissionMapper.ToListItem(e), e.RetainUntil, retentionDays)).ToList(),
                page.ContinuationToken);
        return new SubmissionPage(Array.Empty<SubmissionListItem>(), null);
    }
}

/// <summary>
/// The scan behind the inbox search (#12). The OData filter knows neither contains nor startswith, so a
/// substring search can only be a scan - the trade ContactQueries and HousekeepingQueries already make at
/// this volume. What keeps it bounded is the ceiling, and the ceiling is reported rather than hidden: a
/// result that was cut short has to be able to say so (AC6).
/// </summary>
internal sealed class SearchSubmissionsQuery(TableStorage storage, IOptions<EntriqaOptions> options) : ISearchSubmissionsQuery
{
    public async Task<SubmissionCandidates> ExecuteAsync(string? slug, int scanMax, CancellationToken ct = default)
    {
        var table = await storage.GetAsync("Submissions");
        var query = slug is null
            ? table.QueryAsync<SubmissionEntity>(maxPerPage: 1000, cancellationToken: ct)
            : table.QueryAsync<SubmissionEntity>(e => e.PartitionKey == slug, maxPerPage: 1000, cancellationToken: ct);
        var candidates = new List<SubmissionCandidate>();
        var capped = false;
        var retentionDays = options.Value.RetentionDays;
        await foreach (var e in query)
        {
            // Asked before adding, so the flag means "there was more" rather than "the table happened to
            // hold exactly this many" - the difference between a warning that is true and one that cries wolf.
            if (candidates.Count >= scanMax) { capped = true; break; }
            var candidate = SubmissionMapper.ToCandidate(e);
            candidates.Add(candidate with { Item = SubmissionRetention.Project(candidate.Item, e.RetainUntil, retentionDays) });
        }
        // Row keys are newest-first inside a partition; across partitions the order is the scan's, so the
        // sort happens here - as in ListRecentSubmissionsQuery, and for the same reason.
        return new SubmissionCandidates(
            candidates.OrderByDescending(c => c.Item.CreatedAt).ToList(), candidates.Count, capped);
    }
}
