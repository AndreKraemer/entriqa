namespace Entriqa.Domain.Forms;

/// <summary>
/// The readable form of an appointment (#7): date and time in one time zone and language, the zone named so
/// that an operator reading the visitor's label knows which clock is meant, the title after it.
/// de: <c>Di., 13.10.2026, 10:00–12:00 (Europe/Berlin) · Grundlagen</c>.
/// </summary>
public static class AppointmentLabel
{
    public static string Format(Appointment appointment, TimeZoneInfo zone, string lang) => throw new NotImplementedException("#7");

    /// <summary>The visitor's zone when it is a known IANA id, otherwise <paramref name="fallback"/>.</summary>
    public static TimeZoneInfo ResolveZone(string? requested, string fallback) => throw new NotImplementedException("#7");
}
