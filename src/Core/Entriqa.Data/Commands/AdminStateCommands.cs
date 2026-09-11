using Azure.Data.Tables;
using Entriqa.Application.Ports;
using Entriqa.Data.Entities;

namespace Entriqa.Data.Commands;

internal sealed class SetLastVisitCommand(TableStorage storage) : ISetLastVisitCommand
{
    public async Task ExecuteAsync(string user, DateTimeOffset at, CancellationToken ct = default)
    {
        var table = await storage.GetAsync("AdminState");
        // Merge, not replace: since #13 the same row also carries SeenAt, written by a different path.
        // A replace here would drop it on every "mark everything as seen".
        await table.UpsertEntityAsync(new TableEntity("state", user) { ["LastVisitAt"] = at }, TableUpdateMode.Merge, ct);
    }
}

/// <summary>
/// Notes that an admin has been here (#13) - the row the assignment picker reads. It writes one
/// column and merges, so the last visit stays exactly as it was: that value drives the blue "new
/// since" dot, and a sighting on every inbox load would otherwise silently clear every marker.
/// </summary>
internal sealed class RecordAdminSeenCommand(TableStorage storage) : IRecordAdminSeenCommand
{
    public async Task ExecuteAsync(string user, DateTimeOffset at, CancellationToken ct = default)
    {
        var table = await storage.GetAsync("AdminState");
        await table.UpsertEntityAsync(new TableEntity("state", user) { ["SeenAt"] = at }, TableUpdateMode.Merge, ct);
    }
}

internal sealed class RecordHousekeepingRunCommand(TableStorage storage) : IRecordHousekeepingRunCommand
{
    public async Task ExecuteAsync(DateTimeOffset at, string summary, CancellationToken ct = default)
    {
        var table = await storage.GetAsync("AdminState");
        await table.UpsertEntityAsync(new AdminStateEntity { PartitionKey = "housekeeping", RowKey = "last", LastVisitAt = at, Note = summary },
            TableUpdateMode.Replace, ct);
    }
}
