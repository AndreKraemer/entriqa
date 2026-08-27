using Entriqa.Application.Ports;
using Entriqa.Data.Entities;
using Entriqa.Data.Mapping;
using Entriqa.Domain.Submissions;

namespace Entriqa.Data.Queries;

/// <summary>
/// Scan queries for housekeeping. Deliberately filter scans across the whole table - at this volume
/// (form submissions of two company websites) cheaper than any secondary index construction.
/// </summary>
internal sealed class ListHousekeepingSubmissionsQuery(TableStorage storage) : IListHousekeepingSubmissionsQuery
{
    public async Task<IReadOnlyList<Submission>> ListUnfinishedAsync(DateTimeOffset olderThan, int max, CancellationToken ct = default)
    {
        var table = await storage.GetAsync("Submissions");
        var result = new List<Submission>();
        await foreach (var e in table.QueryAsync<SubmissionEntity>(
            e => (e.State == "processing" || e.State == "failed") && e.CreatedAt < olderThan, cancellationToken: ct))
        {
            result.Add(SubmissionMapper.ToDomain(e));
            if (result.Count >= max) break;
        }
        return result;
    }

    public async Task<IReadOnlyList<Submission>> ListExpiredAsync(DateTimeOffset generalCutoff, DateTimeOffset unconfirmedCutoff, int max, CancellationToken ct = default)
    {
        var table = await storage.GetAsync("Submissions");
        var byId = new Dictionary<string, Submission>();

        await foreach (var e in table.QueryAsync<SubmissionEntity>(e => e.CreatedAt < generalCutoff, cancellationToken: ct))
        {
            var s = SubmissionMapper.ToDomain(e);
            byId[s.Id] = s;
            if (byId.Count >= max) return byId.Values.ToList();
        }
        await foreach (var e in table.QueryAsync<SubmissionEntity>(
            e => e.State == "awaitingconfirmation" && e.CreatedAt < unconfirmedCutoff, cancellationToken: ct))     // Mapper schreibt lowercase
        {
            var s = SubmissionMapper.ToDomain(e);
            byId[s.Id] = s;
            if (byId.Count >= max) break;
        }
        return byId.Values.ToList();
    }
}
