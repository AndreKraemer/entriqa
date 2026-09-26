using Entriqa.Application.Ports;
using Entriqa.Domain.Forms;
using Entriqa.Domain.Submissions;

namespace Entriqa.Application.UseCases;

/// <summary>
/// The appointments a form offers right now (#7): active and not yet begun, earliest first. One place for the
/// published view, the draft view, the submission and the test run, so the visitor never sees a list the
/// server would reject. Forms without an appointment field query nothing.
/// </summary>
public sealed class AppointmentOfferService(IListAppointmentsQuery list, TimeProvider time)
{
    public async Task<IReadOnlyList<Appointment>> ForAsync(FormDefinition def, CancellationToken ct = default)
    {
        if (!def.Fields.Any(f => f.Type == FieldTypes.Appointment)) return Array.Empty<Appointment>();

        var now = time.GetUtcNow();
        var appointments = await list.ExecuteAsync(def.Slug, ct);
        return appointments.Where(a => a.IsOfferedAt(now))
            .OrderBy(a => a.Start).ThenBy(a => a.Id, StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// Freezes the chosen appointment of a <em>validated</em> submission (AC 4, AC 5): the snapshot for the
    /// submission, and its label in place of the bare id in <paramref name="values"/>, so mails, PDF, webhook and
    /// export read a date rather than a key. Null for forms without an appointment field.
    /// </summary>
    public static AppointmentSnapshot? Freeze(FormDefinition def, Dictionary<string, string> values,
        IReadOnlyList<Appointment> offered, TimeZoneInfo zone, string lang)
    {
        if (def.Fields.FirstOrDefault(f => f.Type == FieldTypes.Appointment) is not { } field
            || !values.TryGetValue(field.Id, out var id)) return null;

        var chosen = offered.First(a => a.Id == id);                    // the validator has made sure it is on offer
        var label = AppointmentLabel.Format(chosen, zone, lang);
        values[field.Id] = label;
        return new AppointmentSnapshot(chosen.Id, chosen.Start.ToUniversalTime(), chosen.End?.ToUniversalTime(), chosen.Title, zone.Id, label);
    }
}
