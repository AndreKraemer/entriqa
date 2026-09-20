using Azure;
using Entriqa.Application.Ports;
using Entriqa.Data.Entities;
using Entriqa.Data.Mapping;
using Entriqa.Domain.Forms;

namespace Entriqa.Data.Queries;

// The appointment read adapters (#6). Partition = slug, row = id (see AppointmentMapper).

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
