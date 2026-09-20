using Azure;
using Azure.Data.Tables;
using Entriqa.Application.Ports;
using Entriqa.Data.Mapping;
using Entriqa.Domain.Forms;

namespace Entriqa.Data.Commands;

// The appointment write adapters (#6). Partition = slug, row = id (see AppointmentMapper). No ETag guard:
// each appointment is written whole by the admin, and #6 has no concurrent second writer. Seat counting
// against registrations, which will need care, is #8. Reads live in Queries/AppointmentQueries.cs.

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
