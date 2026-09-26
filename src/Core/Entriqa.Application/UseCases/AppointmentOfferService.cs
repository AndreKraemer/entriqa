using Entriqa.Application.Ports;
using Entriqa.Domain.Forms;

namespace Entriqa.Application.UseCases;

/// <summary>
/// The appointments a form offers right now (#7): active and not yet begun, earliest first. One place for the
/// published view, the draft view, the submission and the test run, so the visitor never sees a list the
/// server would reject. Forms without an appointment field query nothing.
/// </summary>
public sealed class AppointmentOfferService(IListAppointmentsQuery list, TimeProvider time)
{
    public Task<IReadOnlyList<Appointment>> ForAsync(FormDefinition def, CancellationToken ct = default) =>
        def.Fields.Any(f => f.Type == FieldTypes.Appointment)
            ? throw new NotImplementedException($"#7 ({list.GetType().Name}, {time.GetType().Name})")
            : Task.FromResult<IReadOnlyList<Appointment>>(Array.Empty<Appointment>());
}
