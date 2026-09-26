namespace Entriqa.Domain.Forms;

/// <summary>
/// A date an operator offers on a registration form - a webinar session, a workshop slot (#6). Master
/// data of a form <em>slug</em>, never part of the immutable <see cref="FormVersion"/>: changing,
/// cancelling or resizing one must not create a new version (epic #5). Seat counting, the visitor-facing
/// display and registrations themselves are later slices (#7, #8); this type carries only the
/// operator-facing data.
/// </summary>
public sealed record Appointment
{
    /// <summary>The form this appointment belongs to - the partition it is stored under.</summary>
    public required string Slug { get; init; }

    /// <summary>Stable id within the slug, assigned on creation. Empty means "new". It survives every edit,
    /// so a change replaces the same row instead of appending one.</summary>
    public string Id { get; init; } = "";

    /// <summary>Start of the appointment. Carries the UTC offset the admin entered it in; "past" is compared in UTC.</summary>
    public required DateTimeOffset Start { get; init; }

    /// <summary>Optional end. When set, it is always after <see cref="Start"/> (AC 6).</summary>
    public DateTimeOffset? End { get; init; }

    /// <summary>Seats offered. Counting them against registrations is #8; here the number is only recorded.</summary>
    public required int Capacity { get; init; }

    /// <summary>Optional label shown next to the date. The visitor-facing display is #7.</summary>
    public string? Title { get; init; }

    /// <summary>Whether a full appointment may still collect registrations on a waiting list (#8). Off by default.</summary>
    public bool WaitlistEnabled { get; init; }

    /// <summary>A deactivated appointment stays in the admin and keeps its registrations, but is offered
    /// nowhere new on the form (#7). Active by default.</summary>
    public bool Active { get; init; } = true;

    /// <summary>
    /// Whether a visitor may pick this appointment at <paramref name="now"/> (#7, AC 2 and 3): active and not
    /// yet begun. The end plays no part - it only shows up in the label.
    /// </summary>
    public bool IsOfferedAt(DateTimeOffset now) => throw new NotImplementedException("#7");
}
