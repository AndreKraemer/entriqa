namespace Entriqa.Domain.Submissions;

/// <summary>
/// The appointment a submission registers for, copied at submission time (#7). Start and end are UTC;
/// <see cref="TimeZone"/> is the visitor's zone the <see cref="Label"/> was written in. Nothing here points
/// back at the appointment row, so changing or deleting it later leaves the submission readable (AC 5).
/// </summary>
public sealed record AppointmentSnapshot(string Id, DateTimeOffset Start, DateTimeOffset? End, string? Title, string TimeZone, string Label);
