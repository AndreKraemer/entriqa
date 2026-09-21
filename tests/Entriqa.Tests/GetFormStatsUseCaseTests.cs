using Entriqa.Application;
using Entriqa.Application.Ports;
using Entriqa.Application.UseCases;
using Entriqa.Domain.Forms;
using Entriqa.Domain.Submissions;
using Entriqa.Domain.UseCases;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Entriqa.Tests;

/// <summary>
/// Issue #17: a single form's evaluation covers a selectable period, not a fixed 14 days. The per-form
/// use case had no test of its own before (#16 only covered the cross-form one), so these also pin the
/// trend window and the funnel window it divides the completion rate by. The endpoint, the admin markup
/// and the funnel table's own range filter are out of this host's reach and get source guards /
/// acceptance - see the plan on the issue.
/// </summary>
public class GetFormStatsUseCaseTests
{
    private static readonly DateTimeOffset Today = TestData.Time.GetUtcNow();      // 2026-08-21T10:00Z
    private static DateOnly TodayOnly => DateOnly.FromDateTime(Today.UtcDateTime);

    private static Submission Sub(DateTimeOffset createdAt, string? source = null, string? email = null) => new()
    {
        Id = $"kontakt:{Guid.NewGuid():N}",
        Slug = "kontakt",
        Version = 1,
        CreatedAt = createdAt,
        Values = new Dictionary<string, string>(),
        Source = source,
        Email = email,
        StepRuns = { new StepRun { StepId = "s", StepKey = "notify-mail", Phase = StepPhase.OnSubmit, Status = StepRunStatus.Ok } },
    };

    private static (GetFormStatsUseCase UseCase, IGetFunnelTotalsQuery Funnel) Build(
        IReadOnlyList<Submission> submissions, int retentionDays = 180)
    {
        var list = Substitute.For<IListSubmissionsForStatsQuery>();
        list.ExecuteAsync("kontakt", Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(submissions);
        var getPublished = Substitute.For<ITryGetPublishedFormQuery>();
        getPublished.ExecuteAsync("kontakt", Arg.Any<CancellationToken>())
            .Returns(new FormVersion("kontakt", 1, TestData.Contact(), Today, "admin"));
        var getVersion = Substitute.For<ITryGetFormVersionQuery>();
        var funnel = Substitute.For<IGetFunnelTotalsQuery>();
        funnel.ExecuteAsync("kontakt", Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<string, int>());
        var options = Options.Create(new EntriqaOptions { RetentionDays = retentionDays });
        return (new GetFormStatsUseCase(list, getPublished, getVersion, funnel, options, TestData.Time), funnel);
    }

    private static GetFormStatsUseCase UseCase(IReadOnlyList<Submission> submissions, int retentionDays = 180)
        => Build(submissions, retentionDays).UseCase;

    // ---- AC 1: the chosen period drives the trend window ----

    [Fact]
    public async Task GivenAThirtyDayPeriod_WhenGettingStats_ThenTheTrendCoversThirtyDaysAndIgnoresOlderSubmissions()
    {
        var stats = await UseCase(
            [Sub(Today), Sub(Today.AddDays(-29)), Sub(Today.AddDays(-30))])
            .ExecuteAsync("kontakt", null, AnalyticsPeriod.Days30);

        Assert.Equal(30, stats.Daily.Count);
        Assert.Equal(1, stats.Daily[^1]);                                          // today
        Assert.Equal(1, stats.Daily[0]);                                           // 29 days ago
        Assert.Equal(2, stats.Daily.Sum());                                        // the 30-days-ago one is outside
        Assert.Equal(AnalyticsPeriod.Days30, stats.Period);
    }

    // ---- AC 1 (windowed in full, gate-time clarification): the headline count and distributions cover only the period ----

    [Fact]
    public async Task GivenAThirtyDayPeriod_WhenGettingStats_ThenTheCountAndSourceDistributionCoverOnlyThePeriod()
    {
        var stats = await UseCase(
            [
                Sub(Today, source: "linkedin", email: "a@x.de"),
                Sub(Today, source: "linkedin"),
                Sub(Today.AddDays(-29), source: "newsletter"),
                Sub(Today.AddDays(-40), source: "linkedin", email: "b@x.de"),      // outside the 30-day window
            ])
            .ExecuteAsync("kontakt", null, AnalyticsPeriod.Days30);

        Assert.Equal(3, stats.Total);                                              // the 40-days-ago one is excluded
        Assert.Equal(1, stats.WithEmail);                                          // ...including from the e-mail count
        Assert.Equal(2, Assert.Single(stats.Sources, b => b.Label == "linkedin").Count);   // not 3 - the out-of-window linkedin drops
        Assert.Contains(stats.Sources, b => b.Label == "newsletter");
    }

    // ---- AC 1 / #17 Q1: the year buckets the trend by calendar month, twelve buckets ----

    [Fact]
    public async Task GivenTheTwelveMonthPeriod_WhenGettingStats_ThenTheTrendHasTwelveMonthlyBuckets()
    {
        var stats = await UseCase(
            [
                Sub(Today),                                                        // current month
                Sub(new DateTimeOffset(2025, 9, 15, 0, 0, 0, TimeSpan.Zero)),      // first month of the window
                Sub(new DateTimeOffset(2025, 7, 15, 0, 0, 0, TimeSpan.Zero)),      // before the window
            ],
            retentionDays: 400)
            .ExecuteAsync("kontakt", null, AnalyticsPeriod.Months12);

        Assert.Equal(12, stats.Daily.Count);
        Assert.Equal(1, stats.Daily[^1]);                                          // this month
        Assert.Equal(1, stats.Daily[0]);                                           // eleven months ago
        Assert.Equal(2, stats.Daily.Sum());                                        // the pre-window one is not counted
    }

    // ---- AC 4: the funnel is scanned over the very same window the submissions are ----

    [Fact]
    public async Task GivenAThirtyDayPeriod_WhenGettingStats_ThenTheFunnelIsQueriedOverTheSameThirtyDayWindow()
    {
        var (useCase, funnel) = Build([Sub(Today)]);

        await useCase.ExecuteAsync("kontakt", null, AnalyticsPeriod.Days30);

        await funnel.Received(1).ExecuteAsync("kontakt", TodayOnly.AddDays(-29), TodayOnly, Arg.Any<CancellationToken>());
    }

    // ---- AC 2: the offered periods shrink with the retention ----

    [Fact]
    public async Task GivenARetentionShorterThanThirtyDays_WhenGettingStats_ThenOnlyPeriodsWithinTheRetentionAreOffered()
    {
        var stats = await UseCase([Sub(Today)], retentionDays: 20)
            .ExecuteAsync("kontakt", null, AnalyticsPeriods.Default);

        Assert.Equal(new[] { AnalyticsPeriod.Days7, AnalyticsPeriod.Days14 }, stats.AvailablePeriods);
    }

    // ---- The default preserves the previous fixed 14-day behaviour (backward compatibility) ----

    [Fact]
    public async Task GivenTheDefaultPeriod_WhenGettingStats_ThenTheTrendStillCoversFourteenDays()
    {
        var stats = await UseCase(
            [Sub(Today), Sub(Today.AddDays(-13)), Sub(Today.AddDays(-14))])
            .ExecuteAsync("kontakt", null, AnalyticsPeriods.Default);

        Assert.Equal(14, stats.Daily.Count);
        Assert.Equal(2, stats.Daily.Sum());                                        // the 14-days-ago one is outside
    }
}
