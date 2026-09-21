namespace Entriqa.Domain.UseCases;

/// <summary>
/// The window an evaluation ("Auswertung") covers (#17). The day periods are counted back from today
/// inclusive; the year period is the last twelve calendar months and its trend is bucketed by month,
/// not by day.
/// </summary>
public enum AnalyticsPeriod
{
    Days7,
    Days14,
    Days30,
    Days90,
    Months12,
}

/// <summary>
/// The single source of truth for what a period means: its length, its trend buckets, the window it
/// spans and whether it may be offered at all. Centralised so the two stats use cases and the admin
/// never re-derive "14 days" independently (there used to be four such literals).
/// </summary>
public static class AnalyticsPeriods
{
    /// <summary>Every period, in the order the admin offers them.</summary>
    public static readonly IReadOnlyList<AnalyticsPeriod> All =
        new[] { AnalyticsPeriod.Days7, AnalyticsPeriod.Days14, AnalyticsPeriod.Days30, AnalyticsPeriod.Days90, AnalyticsPeriod.Months12 };

    /// <summary>The window used when the admin has not chosen one - the previous fixed behaviour (14 days).</summary>
    public const AnalyticsPeriod Default = AnalyticsPeriod.Days14;

    /// <summary>Nominal length in days; the year counts as 365 for the retention comparison.</summary>
    public static int LengthInDays(this AnalyticsPeriod period) => period switch
    {
        AnalyticsPeriod.Days7 => 7,
        AnalyticsPeriod.Days14 => 14,
        AnalyticsPeriod.Days30 => 30,
        AnalyticsPeriod.Days90 => 90,
        AnalyticsPeriod.Months12 => 365,
        _ => throw new ArgumentOutOfRangeException(nameof(period), period, null),
    };

    /// <summary>Number of trend buckets: one per day for the day periods, twelve for the year.</summary>
    public static int BucketCount(this AnalyticsPeriod period) => period.IsMonthly() ? 12 : period.LengthInDays();

    /// <summary>The year period buckets its trend by calendar month rather than by day.</summary>
    public static bool IsMonthly(this AnalyticsPeriod period) => period == AnalyticsPeriod.Months12;

    /// <summary>
    /// AC 2: the periods offered for a given submission retention, in <see cref="All"/> order - only those
    /// whose length does not exceed <paramref name="retentionDays"/>, so a period never reaches back past
    /// data that housekeeping has already deleted.
    /// </summary>
    public static IReadOnlyList<AnalyticsPeriod> Offered(int retentionDays) =>
        All.Where(p => p.LengthInDays() <= retentionDays).ToList();

    /// <summary>
    /// The period actually used for a request: the chosen one when the retention allows it, otherwise the
    /// longest that does (AC 4 - a period longer than the retention is never computed, whatever asks for
    /// it). Falls back to <see cref="Default"/> only for the degenerate case of a retention below a week.
    /// </summary>
    public static AnalyticsPeriod Clamp(this AnalyticsPeriod period, int retentionDays)
    {
        var offered = Offered(retentionDays);
        if (offered.Contains(period)) return period;
        return offered.Count > 0 ? offered[^1] : Default;
    }

    /// <summary>
    /// The inclusive window [From, To] the period spans, ending on <paramref name="today"/>. Both the
    /// submission trend and the funnel totals are counted over exactly this window (AC 4). The year runs
    /// from the first day of the month eleven months back, so its twelve buckets are whole calendar months.
    /// </summary>
    public static (DateOnly From, DateOnly To) Window(this AnalyticsPeriod period, DateOnly today) =>
        period.IsMonthly()
            ? (new DateOnly(today.Year, today.Month, 1).AddMonths(-11), today)
            : (today.AddDays(-(period.LengthInDays() - 1)), today);

    /// <summary>
    /// The trend bucket a submission dated <paramref name="date"/> falls into for the window ending on
    /// <paramref name="today"/>, or null when it lies outside the window. 0 is the oldest bucket.
    /// </summary>
    public static int? BucketIndex(this AnalyticsPeriod period, DateOnly today, DateOnly date)
    {
        var (from, to) = period.Window(today);
        if (date < from || date > to) return null;
        return period.IsMonthly()
            ? (date.Year - from.Year) * 12 + (date.Month - from.Month)
            : date.DayNumber - from.DayNumber;
    }

    /// <summary>The stable key used on the wire (query parameter and JSON), independent of the enum's ordinal.</summary>
    public static string ToKey(this AnalyticsPeriod period) => period switch
    {
        AnalyticsPeriod.Days7 => "7d",
        AnalyticsPeriod.Days14 => "14d",
        AnalyticsPeriod.Days30 => "30d",
        AnalyticsPeriod.Days90 => "90d",
        AnalyticsPeriod.Months12 => "12m",
        _ => throw new ArgumentOutOfRangeException(nameof(period), period, null),
    };

    /// <summary>Parses <see cref="ToKey"/>; unknown or missing input yields <see cref="Default"/>.</summary>
    public static AnalyticsPeriod ParseOrDefault(string? key) => key?.Trim().ToLowerInvariant() switch
    {
        "7d" => AnalyticsPeriod.Days7,
        "14d" => AnalyticsPeriod.Days14,
        "30d" => AnalyticsPeriod.Days30,
        "90d" => AnalyticsPeriod.Days90,
        "12m" => AnalyticsPeriod.Months12,
        _ => Default,
    };
}
