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
    public static int LengthInDays(this AnalyticsPeriod period) => throw new NotImplementedException();

    /// <summary>Number of trend buckets: one per day for the day periods, twelve for the year.</summary>
    public static int BucketCount(this AnalyticsPeriod period) => throw new NotImplementedException();

    /// <summary>The year period buckets its trend by calendar month rather than by day.</summary>
    public static bool IsMonthly(this AnalyticsPeriod period) => throw new NotImplementedException();

    /// <summary>
    /// AC 2: the periods offered for a given submission retention, in <see cref="All"/> order - only those
    /// whose length does not exceed <paramref name="retentionDays"/>, so a period never reaches back past
    /// data that housekeeping has already deleted.
    /// </summary>
    public static IReadOnlyList<AnalyticsPeriod> Offered(int retentionDays) => throw new NotImplementedException();

    /// <summary>
    /// The inclusive window [From, To] the period spans, ending on <paramref name="today"/>. Both the
    /// submission trend and the funnel totals are counted over exactly this window (AC 4).
    /// </summary>
    public static (DateOnly From, DateOnly To) Window(this AnalyticsPeriod period, DateOnly today) => throw new NotImplementedException();

    /// <summary>
    /// The trend bucket a submission dated <paramref name="date"/> falls into for the window ending on
    /// <paramref name="today"/>, or null when it lies outside the window. 0 is the oldest bucket.
    /// </summary>
    public static int? BucketIndex(this AnalyticsPeriod period, DateOnly today, DateOnly date) => throw new NotImplementedException();

    /// <summary>The stable key used on the wire (query parameter and JSON), independent of the enum's ordinal.</summary>
    public static string ToKey(this AnalyticsPeriod period) => throw new NotImplementedException();

    /// <summary>Parses <see cref="ToKey"/>; unknown or missing input yields <see cref="Default"/>.</summary>
    public static AnalyticsPeriod ParseOrDefault(string? key) => throw new NotImplementedException();
}
