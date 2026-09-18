namespace Entriqa.Admin.Services;

/// <summary>
/// The inbox's "expiring soon" rule (AC2 of #15): highlighted from here, not read straight off
/// DateTimeOffset.UtcNow in the markup, so it executes under a unit test rather than only being
/// scanned as source. Deliberately free of Blazor types, like <see cref="SubmissionSelection"/>.
/// </summary>
public static class SubmissionRetentionDisplay
{
    public const int HighlightWithinDays = 14;

    /// <summary>True for what expires within the window - never for what already has (housekeeping would
    /// have removed it by the next run) and never for a permanent retention.</summary>
    public static bool ExpiresWithinHighlightWindow(SubmissionListItem item, DateTimeOffset now) =>
        !item.RetainedIndefinitely && item.ExpiresAt > now
        && item.ExpiresAt - now <= TimeSpan.FromDays(HighlightWithinDays);

    public static int DaysUntilExpiry(SubmissionListItem item, DateTimeOffset now) =>
        (int)Math.Ceiling((item.ExpiresAt - now).TotalDays);
}
