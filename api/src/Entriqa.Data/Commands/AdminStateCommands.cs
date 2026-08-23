using Azure.Data.Tables;
using Entriqa.Application.Ports;
using Entriqa.Data.Entities;

namespace Entriqa.Data.Commands;

internal sealed class SetLastVisitCommand(TableStorage storage) : ISetLastVisitCommand
{
    public async Task ExecuteAsync(string user, DateTimeOffset at, CancellationToken ct = default)
    {
        var table = await storage.GetAsync("AdminState");
        await table.UpsertEntityAsync(new AdminStateEntity { RowKey = user, LastVisitAt = at }, TableUpdateMode.Replace, ct);
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
