using Azure;
using Azure.Data.Tables;
using Entriqa.Application.Ports;
using Entriqa.Data.Entities;
using Entriqa.Data.Mapping;
using Entriqa.Domain.Forms;

namespace Entriqa.Data.Commands;

// The appointment table adapters (#6). Partition = slug, row = id (see AppointmentMapper). No ETag guard:
// each appointment is written whole by the admin, and #6 has no concurrent second writer. Seat counting
// against registrations, which will need care, is #8.

internal sealed class ListAppointmentsQuery(TableStorage storage) : IListAppointmentsQuery
{
    public async Task<IReadOnlyList<Appointment>> ExecuteAsync(string slug, CancellationToken ct = default)
    {
        var table = await storage.GetAsync("Appointments");
        var result = new List<Appointment>();
        await foreach (var e in table.QueryAsync<AppointmentEntity>(e => e.PartitionKey == slug, cancellationToken: ct))
            result.Add(AppointmentMapper.ToDomain(e));
        return result;
    }
}

internal sealed class TryGetAppointmentQuery(TableStorage storage) : ITryGetAppointmentQuery
{
    public async Task<Appointment?> ExecuteAsync(string slug, string id, CancellationToken ct = default)
    {
        var table = await storage.GetAsync("Appointments");
        try
        {
            var e = await table.GetEntityAsync<AppointmentEntity>(slug, id, cancellationToken: ct);
            return AppointmentMapper.ToDomain(e.Value);
        }
        catch (RequestFailedException ex) when (ex.Status == 404) { return null; }
    }
}

internal sealed class SaveAppointmentCommand(TableStorage storage) : ISaveAppointmentCommand
{
    public async Task ExecuteAsync(Appointment appointment, CancellationToken ct = default)
    {
        var table = await storage.GetAsync("Appointments");
        await table.UpsertEntityAsync(AppointmentMapper.ToEntity(appointment), TableUpdateMode.Replace, ct);
    }
}

internal sealed class DeleteAppointmentCommand(TableStorage storage) : IDeleteAppointmentCommand
{
    public async Task ExecuteAsync(string slug, string id, CancellationToken ct = default)
    {
        var table = await storage.GetAsync("Appointments");
        await table.DeleteEntityAsync(slug, id, ETag.All, ct);       // 404 does not throw - idempotent
    }
}
