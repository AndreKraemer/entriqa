using Entriqa.Application.Ports;
using Entriqa.Domain.Forms;

namespace Entriqa.Data.Commands;

// Skeleton (#6): the appointment table adapters. Partition = slug, row = id (see AppointmentMapper).
// Registered by the Command/Query suffix scan. Bodies throw until #6 is implemented.

internal sealed class ListAppointmentsQuery(TableStorage storage) : IListAppointmentsQuery
{
    private readonly TableStorage _storage = storage;

    public Task<IReadOnlyList<Appointment>> ExecuteAsync(string slug, CancellationToken ct = default) =>
        throw new NotImplementedException();
}

internal sealed class TryGetAppointmentQuery(TableStorage storage) : ITryGetAppointmentQuery
{
    private readonly TableStorage _storage = storage;

    public Task<Appointment?> ExecuteAsync(string slug, string id, CancellationToken ct = default) =>
        throw new NotImplementedException();
}

internal sealed class SaveAppointmentCommand(TableStorage storage) : ISaveAppointmentCommand
{
    private readonly TableStorage _storage = storage;

    public Task ExecuteAsync(Appointment appointment, CancellationToken ct = default) =>
        throw new NotImplementedException();
}

internal sealed class DeleteAppointmentCommand(TableStorage storage) : IDeleteAppointmentCommand
{
    private readonly TableStorage _storage = storage;

    public Task ExecuteAsync(string slug, string id, CancellationToken ct = default) =>
        throw new NotImplementedException();
}
