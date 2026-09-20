using Entriqa.Application.Ports;
using Entriqa.Domain.Errors;
using Entriqa.Domain.Forms;
using Entriqa.Domain.UseCases;

namespace Entriqa.Application.UseCases;

// Appointments of a form (#6): master data of a slug, never part of a FormVersion. Create, list,
// deactivate, delete. Seat counting, the visitor view and registrations are #7/#8.

internal sealed class ListAppointmentsUseCase(IListAppointmentsQuery list) : IListAppointmentsUseCase
{
    public async Task<IReadOnlyList<Appointment>> ExecuteAsync(string slug, CancellationToken ct = default)
    {
        var appointments = await list.ExecuteAsync(slug, ct);
        return appointments.OrderBy(a => a.Start).ThenBy(a => a.Id, StringComparer.Ordinal).ToList();
    }
}

internal sealed class SaveAppointmentUseCase(ISaveAppointmentCommand save) : ISaveAppointmentUseCase
{
    public async Task<Appointment> ExecuteAsync(Appointment appointment, CancellationToken ct = default)
    {
        if (appointment.End is { } end && end <= appointment.Start)
            throw new AppException(ErrorCodes.Validation, ErrorMessages.AppointmentEndBeforeStart);

        // Empty id means "new": assign a stable one so a later edit replaces the same row (AC 2).
        var stored = string.IsNullOrEmpty(appointment.Id)
            ? appointment with { Id = Guid.NewGuid().ToString("n") }
            : appointment;

        await save.ExecuteAsync(stored, ct);
        return stored;
    }
}

internal sealed class DeactivateAppointmentUseCase(ITryGetAppointmentQuery get, ISaveAppointmentCommand save) : IDeactivateAppointmentUseCase
{
    public async Task ExecuteAsync(string slug, string id, CancellationToken ct = default)
    {
        var appointment = await get.ExecuteAsync(slug, id, ct)
            ?? throw new NotFoundException(ErrorCodes.AppointmentNotFound, ErrorMessages.AppointmentNotFound);

        // Deactivating keeps the row and its identity - it stays in the admin and keeps its registrations (AC 4).
        await save.ExecuteAsync(appointment with { Active = false }, ct);
    }
}

internal sealed class DeleteAppointmentUseCase(IDeleteAppointmentCommand delete) : IDeleteAppointmentUseCase
{
    public Task ExecuteAsync(string slug, string id, CancellationToken ct = default) => delete.ExecuteAsync(slug, id, ct);
}
