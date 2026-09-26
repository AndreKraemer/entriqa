using System.Globalization;

namespace Entriqa.Domain.Forms;

/// <summary>
/// The readable form of an appointment (#7): date and time in one time zone and language, the zone named so
/// that an operator reading the visitor's label knows which clock is meant, the title after it.
/// de: <c>Di., 13.10.2026, 10:00–12:00 (Europe/Berlin) · Grundlagen</c>.
/// </summary>
public static class AppointmentLabel
{
    // Day and month names are spelled out rather than taken from a culture: Windows' NLS and Linux' ICU abbreviate
    // German weekdays differently ("Di" vs "Di."), and the label is stored - it must not depend on the host.
    private static readonly string[] GermanDays = ["So.", "Mo.", "Di.", "Mi.", "Do.", "Fr.", "Sa."];
    private static readonly string[] EnglishDays = ["Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat"];
    private static readonly string[] EnglishMonths = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];

    public static string Format(Appointment appointment, TimeZoneInfo zone, string lang)
    {
        // Anything but English reads German, like the rest of the visitor texts.
        string Date(DateTimeOffset t) => lang == "en"
            ? $"{EnglishDays[(int)t.DayOfWeek]}, {t.Day} {EnglishMonths[t.Month - 1]} {t.Year}"
            : $"{GermanDays[(int)t.DayOfWeek]}, {t.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture)}";
        static string Time(DateTimeOffset t) => t.ToString("HH:mm", CultureInfo.InvariantCulture);

        var start = TimeZoneInfo.ConvertTime(appointment.Start, zone);
        var when = $"{Date(start)}, {Time(start)}";
        if (appointment.End is { } endUtc)
        {
            var end = TimeZoneInfo.ConvertTime(endUtc, zone);
            when += end.Date == start.Date ? $"–{Time(end)}" : $" – {Date(end)}, {Time(end)}";
        }

        var label = $"{when} ({zone.Id})";
        return string.IsNullOrWhiteSpace(appointment.Title) ? label : $"{label} · {appointment.Title.Trim()}";
    }

    /// <summary>The visitor's zone when it is a known IANA id, otherwise <paramref name="fallback"/> (UTC if that is unknown too).</summary>
    public static TimeZoneInfo ResolveZone(string? requested, string fallback)
    {
        if (!string.IsNullOrWhiteSpace(requested) && TimeZoneInfo.TryFindSystemTimeZoneById(requested, out var zone)) return zone;
        return TimeZoneInfo.TryFindSystemTimeZoneById(fallback, out var configured) ? configured : TimeZoneInfo.Utc;
    }
}
