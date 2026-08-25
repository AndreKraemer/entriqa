using Entriqa.Application.Ports;
using Entriqa.Data.Entities;
using Entriqa.Data.Mapping;
using Entriqa.Domain.Submissions;

namespace Entriqa.Data.Queries;

/// <summary>
/// Contact view: deliberately a scan across the submission table instead of an index table.
/// The volume is small and the retention period (RetentionDays) caps the amount anyway;
/// an index can come once the numbers ask for it.
/// </summary>
internal sealed class ListContactSubmissionsQuery(TableStorage storage) : IListContactSubmissionsQuery
{
    public async Task<IReadOnlyList<Submission>> ListWithEmailAsync(int max, CancellationToken ct = default)
    {
        var table = await storage.GetAsync("Submissions");
        var result = new List<Submission>();
        await foreach (var e in table.QueryAsync<SubmissionEntity>(maxPerPage: 1000, cancellationToken: ct))
        {
            if (string.IsNullOrEmpty(e.Email)) continue;
            result.Add(SubmissionMapper.ToDomain(e));
            if (result.Count >= max) break;
        }
        return result;
    }

    public async Task<IReadOnlyList<SubmissionListItem>> ListByEmailAsync(string email, CancellationToken ct = default)
    {
        var table = await storage.GetAsync("Submissions");
        var result = new List<SubmissionListItem>();
        await foreach (var e in table.QueryAsync<SubmissionEntity>(maxPerPage: 1000, cancellationToken: ct))
        {
            if (e.Email is null || !e.Email.Equals(email, StringComparison.OrdinalIgnoreCase)) continue;
            result.Add(SubmissionMapper.ToListItem(e));
        }
        return result.OrderByDescending(s => s.CreatedAt).ToList();
    }
}
