using Entriqa.Application.Ports;
using Entriqa.Domain.Forms;
using Entriqa.Domain.UseCases;

namespace Entriqa.Application.UseCases;

// Skeleton (#6): the public shape the red tests drive. No logic yet - every body throws.

internal sealed class ListAppointmentsUseCase(IListAppointmentsQuery list) : IListAppointmentsUseCase
{
    private readonly IListAppointmentsQuery _list = list;

    public Task<IReadOnlyList<Appointment>> ExecuteAsync(string slug, CancellationToken ct = default) =>
        throw new NotImplementedException();
}

internal sealed class SaveAppointmentUseCase(ISaveAppointmentCommand save) : ISaveAppointmentUseCase
{
    private readonly ISaveAppointmentCommand _save = save;

    public Task<Appointment> ExecuteAsync(Appointment appointment, CancellationToken ct = default) =>
        throw new NotImplementedException();
}

internal sealed class DeactivateAppointmentUseCase(ITryGetAppointmentQuery get, ISaveAppointmentCommand save) : IDeactivateAppointmentUseCase
{
    private readonly ITryGetAppointmentQuery _get = get;
    private readonly ISaveAppointmentCommand _save = save;

    public Task ExecuteAsync(string slug, string id, CancellationToken ct = default) =>
        throw new NotImplementedException();
}

internal sealed class DeleteAppointmentUseCase(IDeleteAppointmentCommand delete) : IDeleteAppointmentUseCase
{
    private readonly IDeleteAppointmentCommand _delete = delete;

    public Task ExecuteAsync(string slug, string id, CancellationToken ct = default) =>
        throw new NotImplementedException();
}
