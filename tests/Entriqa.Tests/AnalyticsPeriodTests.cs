using Entriqa.Domain.UseCases;
using Xunit;

namespace Entriqa.Tests;

/// <summary>
/// Issue #17: the window an evaluation covers is selectable. The rules that decide which periods may be
/// offered (AC 2) and how a period maps submissions onto trend buckets (AC 1) live in
/// <see cref="AnalyticsPeriods"/> so both stats use cases and the admin share one definition.
/// </summary>
public class AnalyticsPeriodTests
{
    private static readonly DateOnly Today = new(2026, 8, 21);

    // ---- AC 2: a period is offered only while it stays within the submission retention ----

    [Fact]
    public void GivenTheDefaultRetentionOf180Days_WhenOfferingPeriods_ThenTheTwelveMonthPeriodIsNotOffered()
    {
        Assert.Equal(
            new[] { AnalyticsPeriod.Days7, AnalyticsPeriod.Days14, AnalyticsPeriod.Days30, AnalyticsPeriod.Days90 },
            AnalyticsPeriods.Offered(180));
    }

    [Fact]
    public void GivenARetentionOf20Days_WhenOfferingPeriods_ThenOnlySevenAndFourteenDaysRemain()
    {
        Assert.Equal(new[] { AnalyticsPeriod.Days7, AnalyticsPeriod.Days14 }, AnalyticsPeriods.Offered(20));
    }

    [Fact]
    public void GivenARetentionOfAFullYear_WhenOfferingPeriods_ThenEveryPeriodIncludingTwelveMonthsIsOffered()
    {
        Assert.Equal(AnalyticsPeriods.All, AnalyticsPeriods.Offered(365));
    }

    // ---- AC 4: a period longer than the retention is never computed, whatever asks for it ----

    [Fact]
    public void GivenAPeriodBeyondTheRetention_WhenClamping_ThenItFallsToTheLongestOfferedPeriod()
    {
        Assert.Equal(AnalyticsPeriod.Days90, AnalyticsPeriod.Months12.Clamp(180));   // 12mo asked, only up to 90 fits
        Assert.Equal(AnalyticsPeriod.Days14, AnalyticsPeriod.Days90.Clamp(20));      // 90d asked, only up to 14 fits
    }

    [Fact]
    public void GivenAPeriodWithinTheRetention_WhenClamping_ThenItIsKept()
    {
        Assert.Equal(AnalyticsPeriod.Days30, AnalyticsPeriod.Days30.Clamp(180));
        Assert.Equal(AnalyticsPeriod.Months12, AnalyticsPeriod.Months12.Clamp(400));
    }

    // ---- AC 1: the day periods bucket by day ----

    [Fact]
    public void GivenAThirtyDayPeriod_WhenBucketing_ThenItSpansThirtyDailyBucketsEndingToday()
    {
        Assert.Equal(30, AnalyticsPeriod.Days30.BucketCount());
        Assert.Equal(29, AnalyticsPeriod.Days30.BucketIndex(Today, Today));                       // today is the last bucket
        Assert.Equal(0, AnalyticsPeriod.Days30.BucketIndex(Today, Today.AddDays(-29)));           // 29 days ago is the first
        Assert.Null(AnalyticsPeriod.Days30.BucketIndex(Today, Today.AddDays(-30)));               // 30 days ago is outside
    }

    // ---- AC 1 / #17 Q1: the year period buckets by calendar month, twelve buckets ----

    [Fact]
    public void GivenTheTwelveMonthPeriod_WhenBucketing_ThenItSpansTwelveMonthlyBucketsByCalendarMonth()
    {
        Assert.Equal(12, AnalyticsPeriod.Months12.BucketCount());
        Assert.True(AnalyticsPeriod.Months12.IsMonthly());
        Assert.Equal(11, AnalyticsPeriod.Months12.BucketIndex(Today, new DateOnly(2026, 8, 5)));  // current month is the last bucket
        Assert.Equal(0, AnalyticsPeriod.Months12.BucketIndex(Today, new DateOnly(2025, 9, 1)));   // first of the window
        Assert.Null(AnalyticsPeriod.Months12.BucketIndex(Today, new DateOnly(2025, 8, 31)));      // the day before the window is outside
    }

    // ---- The wire key survives the round trip and falls back safely (query parameter + JSON plumbing) ----

    [Theory]
    [InlineData(AnalyticsPeriod.Days7)]
    [InlineData(AnalyticsPeriod.Days14)]
    [InlineData(AnalyticsPeriod.Days30)]
    [InlineData(AnalyticsPeriod.Days90)]
    [InlineData(AnalyticsPeriod.Months12)]
    public void GivenAPeriod_WhenRoundTrippingItsWireKey_ThenItParsesBackToTheSamePeriod(AnalyticsPeriod period)
    {
        Assert.Equal(period, AnalyticsPeriods.ParseOrDefault(period.ToKey()));
    }

    [Fact]
    public void GivenAnUnknownOrMissingWireKey_WhenParsing_ThenItFallsBackToTheDefault()
    {
        Assert.Equal(AnalyticsPeriods.Default, AnalyticsPeriods.ParseOrDefault("nonsense"));
        Assert.Equal(AnalyticsPeriods.Default, AnalyticsPeriods.ParseOrDefault(null));
    }

    // ---- The default preserves the previous fixed behaviour (backward compatibility) ----

    [Fact]
    public void GivenNoChoice_WhenTakingTheDefaultPeriod_ThenItIsFourteenDays()
    {
        Assert.Equal(AnalyticsPeriod.Days14, AnalyticsPeriods.Default);
    }
}
