using Entriqa.Application;
using Entriqa.Application.Ports;
using Entriqa.Application.UseCases;
using Entriqa.Domain.Quiz;
using Entriqa.Domain.Submissions;
using Entriqa.Domain.UseCases;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Entriqa.Tests;

/// <summary>
/// Issue #16: with no single form chosen the evaluation shows figures across all published forms.
/// This is a separate use case from <c>GetFormStatsUseCase</c>: it needs no form definition (there is
/// none across forms) and carries no quiz metrics (criterion 3 - questions, options and results are
/// per-form and do not compare across forms).
///
/// The aggregation runs in <see cref="GetOverallStatsUseCase"/>, above the ports, so it is executed
/// here. The cross-partition scan, the funnel scan, the endpoint and the page markup are out of reach
/// of this host and get their own coverage (source guards / acceptance) - see the plan on the issue.
/// The DOI-Quote itself (Confirmed / with-email) is rendered in the page; here we assert the counts it
/// divides.
/// </summary>
public class GetOverallStatsUseCaseTests
{
    private static readonly DateTimeOffset Today = TestData.Time.GetUtcNow();   // 2026-08-21T10:00Z

    private static FormListItem Published(string slug, string name, string type = "contact") =>
        new(slug, name, type, "published", 1, Today);

    private static FormListItem Draft(string slug, string name) =>
        new(slug, name, "contact", "draft", 0, Today);

    /// <summary>A submission of <paramref name="slug"/>, its state driven by one step run so that Failed
    /// and awaiting-confirmation are reachable exactly as <see cref="Submission.State"/> derives them.</summary>
    private static Submission Sub(
        string slug, int daysAgo = 0, string? email = null, bool confirmed = false,
        StepRunStatus status = StepRunStatus.Ok, string? source = null, QuizOutcome? quiz = null)
    {
        var created = Today.AddDays(-daysAgo);
        return new Submission
        {
            Id = $"{slug}:{Guid.NewGuid():N}",
            Slug = slug,
            Version = 1,
            CreatedAt = created,
            Values = new Dictionary<string, string>(),
            Email = email,
            Source = source,
            Quiz = quiz,
            ConfirmedAt = confirmed ? created : null,
            StepRuns = { new StepRun { StepId = "s", StepKey = "notify-mail", Phase = StepPhase.OnSubmit, Status = status } },
        };
    }

    private static GetOverallStatsUseCase UseCase(
        IReadOnlyList<Submission> submissions,
        IReadOnlyList<FormListItem> forms,
        IReadOnlyDictionary<string, FunnelStats>? funnel = null,
        int retentionDays = 180)
        => Build(submissions, forms, funnel, retentionDays).UseCase;

    private static (GetOverallStatsUseCase UseCase, IGetAllFunnelTotalsQuery Funnel) Build(
        IReadOnlyList<Submission> submissions,
        IReadOnlyList<FormListItem> forms,
        IReadOnlyDictionary<string, FunnelStats>? funnel = null,
        int retentionDays = 180)
    {
        var list = Substitute.For<IListAllSubmissionsForStatsQuery>();
        list.ExecuteAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(submissions);
        var formsQuery = Substitute.For<IListFormsQuery>();
        formsQuery.ExecuteAsync(Arg.Any<CancellationToken>()).Returns(forms);
        var funnelQuery = Substitute.For<IGetAllFunnelTotalsQuery>();
        funnelQuery.ExecuteAsync(Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(funnel ?? new Dictionary<string, FunnelStats>());
        var options = Options.Create(new EntriqaOptions { RetentionDays = retentionDays });
        return (new GetOverallStatsUseCase(list, formsQuery, funnelQuery, options, TestData.Time), funnelQuery);
    }

    // ---- Criterion 2: the cross-form figures ----

    [Fact]
    public async Task GivenSubmissionsAcrossPublishedForms_WhenExecuting_ThenTotalsAndDoiAndFailedAggregateAcrossForms()
    {
        var stats = await UseCase(
            [
                Sub("kontakt", email: "a@x.de", confirmed: true),
                Sub("kontakt", email: "b@x.de"),                                  // has email, awaiting
                Sub("whitepaper", email: "c@x.de", confirmed: true),
                Sub("whitepaper", status: StepRunStatus.Failed),                  // no email, failed
                Sub("whitepaper", status: StepRunStatus.Waiting, email: "d@x.de"),// awaiting confirmation
            ],
            [Published("kontakt", "Kontakt"), Published("whitepaper", "Whitepaper", "leadmagnet")])
            .ExecuteAsync(AnalyticsPeriods.Default);

        Assert.Equal(5, stats.Total);
        Assert.Equal(4, stats.WithEmail);
        Assert.Equal(2, stats.Confirmed);
        Assert.Equal(1, stats.AwaitingConfirmation);
        Assert.Equal(1, stats.Failed);
    }

    [Fact]
    public async Task GivenSubmissionsOnDifferentDays_WhenExecuting_ThenDailyTrendSumsAllFormsWithinTheWindow()
    {
        var stats = await UseCase(
            [
                Sub("kontakt", daysAgo: 0),
                Sub("whitepaper", daysAgo: 0),
                Sub("kontakt", daysAgo: 13),
                Sub("kontakt", daysAgo: 20),                                      // outside the 14-day window
            ],
            [Published("kontakt", "Kontakt"), Published("whitepaper", "Whitepaper")])
            .ExecuteAsync(AnalyticsPeriods.Default);

        Assert.Equal(14, stats.Daily.Count);
        Assert.Equal(2, stats.Daily[13]);                                          // today, both forms
        Assert.Equal(1, stats.Daily[0]);                                           // 13 days ago
        Assert.Equal(3, stats.Daily.Sum());                                        // the 20-days-ago one is not counted
    }

    [Fact]
    public async Task GivenSourcesAcrossForms_WhenExecuting_ThenSourcesAreGroupedAcrossAllFormsDescending()
    {
        var stats = await UseCase(
            [
                Sub("kontakt", source: "linkedin"),
                Sub("whitepaper", source: "linkedin"),
                Sub("kontakt", source: "newsletter"),
                Sub("kontakt"),                                                    // no source - not a bar
            ],
            [Published("kontakt", "Kontakt"), Published("whitepaper", "Whitepaper")])
            .ExecuteAsync(AnalyticsPeriods.Default);

        Assert.Equal(new[] { "linkedin", "newsletter" }, stats.Sources.Select(b => b.Label));
        Assert.Equal(2, stats.Sources[0].Count);
        Assert.Equal(1, stats.Sources[1].Count);
    }

    [Fact]
    public async Task GivenFormsWithDifferentVolume_WhenExecuting_ThenFormsAreRankedBySubmissionsDescendingWithNames()
    {
        var stats = await UseCase(
            [
                Sub("whitepaper"), Sub("whitepaper"), Sub("whitepaper"),           // 3
                Sub("kontakt"),                                                    // 1
                Sub("entwurf"),                                                    // belongs to a draft form - must not count
            ],
            [Published("kontakt", "Kontakt"), Published("whitepaper", "Whitepaper"), Draft("entwurf", "Entwurf")])
            .ExecuteAsync(AnalyticsPeriods.Default);

        Assert.Equal(new[] { "Whitepaper", "Kontakt" }, stats.Forms.Select(f => f.Name));
        Assert.Equal(3, stats.Forms[0].Total);
        Assert.Equal(1, stats.Forms[1].Total);
        Assert.DoesNotContain(stats.Forms, f => f.Slug == "entwurf");              // draft form excluded (criterion 1: published only)
        Assert.Equal(4, stats.Total);                                              // the draft form's submission is not counted
    }

    [Fact]
    public async Task GivenFunnelTotalsPerForm_WhenExecuting_ThenEachFormCarriesItsViewsAndCompletionInputs()
    {
        var stats = await UseCase(
            [Sub("kontakt", daysAgo: 0), Sub("kontakt", daysAgo: 1)],
            [Published("kontakt", "Kontakt")],
            new Dictionary<string, FunnelStats> { ["kontakt"] = new FunnelStats(Views: 40, Starts: 10) })
            .ExecuteAsync(AnalyticsPeriods.Default);

        var kontakt = Assert.Single(stats.Forms);
        Assert.Equal(40, kontakt.Views);
        Assert.Equal(10, kontakt.Starts);
        Assert.Equal(2, kontakt.Recent);                                           // 14-day submissions, the completion numerator
    }

    // ---- Criterion 5: a published form without submissions still appears ----

    [Fact]
    public async Task GivenAPublishedFormWithoutSubmissions_WhenExecuting_ThenItAppearsWithZeroNotMissing()
    {
        var stats = await UseCase(
            [Sub("kontakt")],
            [Published("kontakt", "Kontakt"), Published("leer", "Leeres Formular")])
            .ExecuteAsync(AnalyticsPeriods.Default);

        var empty = Assert.Single(stats.Forms, f => f.Slug == "leer");
        Assert.Equal(0, empty.Total);
        Assert.Equal(0, empty.Recent);
        Assert.Equal(0, empty.Views);
    }

    // ---- Criterion 3: a quiz form is an ordinary form here, with no quiz breakdown ----

    [Fact]
    public async Task GivenAPublishedQuizFormWithSubmissions_WhenExecuting_ThenItsSubmissionsCountAndNoQuizFiguresAreProduced()
    {
        var quiz = new QuizOutcome(new Dictionary<string, string> { ["q1"] = "o1" }, ["q1"], 3, 5, 60, "r1", false);
        var stats = await UseCase(
            [
                Sub("selbsttest", source: "linkedin", quiz: quiz),
                Sub("selbsttest", quiz: quiz),
                Sub("kontakt"),
            ],
            [Published("kontakt", "Kontakt"), Published("selbsttest", "Selbsttest", "quiz")])
            .ExecuteAsync(AnalyticsPeriods.Default);

        Assert.Equal(3, stats.Total);                                              // quiz submissions counted as ordinary
        Assert.Equal(2, Assert.Single(stats.Forms, f => f.Slug == "selbsttest").Total);
        Assert.Contains(stats.Sources, b => b.Label == "linkedin");                // the quiz submission's source still counts
        // No quiz breakdown exists to assert against: OverallStats carries no QuizStats member by design (criterion 3).
    }

    // ---- Criterion 6: no published form at all ----

    [Fact]
    public async Task GivenNoPublishedForm_WhenExecuting_ThenFormsIsEmpty()
    {
        // VACUOUSLY GREEN against the skeleton (it returns empty Forms). Debt recorded on the plan comment:
        // a use case that returns empty Forms unconditionally (ignoring IListFormsQuery) also passes this,
        // but fails the ranking test and the zero-submission test above - they are its discriminating backstop.
        var stats = await UseCase([], [Draft("entwurf", "Entwurf")]).ExecuteAsync(AnalyticsPeriods.Default);

        Assert.Empty(stats.Forms);
    }

    // ---- #17 Criterion 2: the offered periods shrink with the retention ----

    [Fact]
    public async Task GivenARetentionShorterThanThirtyDays_WhenExecuting_ThenOnlyPeriodsWithinTheRetentionAreOffered()
    {
        var stats = await UseCase([], [Published("kontakt", "Kontakt")], retentionDays: 20)
            .ExecuteAsync(AnalyticsPeriods.Default);

        Assert.Equal(new[] { AnalyticsPeriod.Days7, AnalyticsPeriod.Days14 }, stats.AvailablePeriods);
    }

    // ---- #17 Criterion 1: a chosen period drives the trend window ----

    [Fact]
    public async Task GivenAThirtyDayPeriod_WhenExecuting_ThenTheTrendCoversThirtyDaysAndIgnoresOlderSubmissions()
    {
        var stats = await UseCase(
            [
                Sub("kontakt", daysAgo: 0),
                Sub("kontakt", daysAgo: 29),
                Sub("kontakt", daysAgo: 30),                                       // outside the 30-day window
            ],
            [Published("kontakt", "Kontakt")])
            .ExecuteAsync(AnalyticsPeriod.Days30);

        Assert.Equal(30, stats.Daily.Count);
        Assert.Equal(1, stats.Daily[^1]);                                          // today
        Assert.Equal(1, stats.Daily[0]);                                           // 29 days ago
        Assert.Equal(2, stats.Daily.Sum());                                        // the 30-days-ago one is not counted
        Assert.Equal(AnalyticsPeriod.Days30, stats.Period);
    }

    // ---- #17 (gate decision): the cross-form headline totals stay all-time; only the trend windows ----

    [Fact]
    public async Task GivenAShortPeriodWithOlderSubmissions_WhenExecuting_ThenTheHeadlineTotalStaysAllTimeWhileTheTrendWindows()
    {
        var stats = await UseCase(
            [Sub("kontakt", daysAgo: 0), Sub("kontakt", daysAgo: 40)],              // one inside, one before a 30-day window
            [Published("kontakt", "Kontakt")])
            .ExecuteAsync(AnalyticsPeriod.Days30);

        Assert.Equal(2, stats.Total);                                               // "gesamt" tile: all-time, the 40-days-ago one included
        Assert.Equal(1, stats.Daily.Sum());                                         // trend: only the in-window one
    }

    // ---- #17 Criterion 4: the funnel is scanned over the very same window as the submissions ----

    [Fact]
    public async Task GivenAThirtyDayPeriod_WhenExecuting_ThenTheFunnelIsScannedOverThatWindowNotFourteenDays()
    {
        var (useCase, funnel) = Build([], [Published("kontakt", "Kontakt")]);

        await useCase.ExecuteAsync(AnalyticsPeriod.Days30);

        var today = DateOnly.FromDateTime(TestData.Time.GetUtcNow().UtcDateTime);
        await funnel.Received(1).ExecuteAsync(today.AddDays(-29), today, Arg.Any<CancellationToken>());
    }
}
