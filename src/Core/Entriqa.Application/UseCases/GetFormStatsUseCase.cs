using Entriqa.Application.Ports;
using Entriqa.Domain.Errors;
using Entriqa.Domain.Forms;
using Entriqa.Domain.Submissions;
using Entriqa.Domain.UseCases;
using Microsoft.Extensions.Options;

namespace Entriqa.Application.UseCases;

/// <summary>
/// Statistics computed in one pass from the submissions of the form. Question and option texts come from
/// the respective version (default locale); "n seen" per question = answers on the path actually taken.
/// </summary>
internal sealed class GetFormStatsUseCase(
    IListSubmissionsForStatsQuery list,
    ITryGetPublishedFormQuery getPublished,
    ITryGetFormVersionQuery getVersion,
    IGetFunnelTotalsQuery funnel,
    IOptions<EntriqaOptions> options,
    TimeProvider time) : IGetFormStatsUseCase
{
    public async Task<FormStats> ExecuteAsync(string slug, int? version, AnalyticsPeriod period, CancellationToken ct = default)
    {
        var all = await list.ExecuteAsync(slug, 5000, ct);
        var versions = all.Select(s => s.Version).Distinct().OrderBy(v => v).ToList();   // all-time: the version filter's choices
        var byVersion = version is { } v ? all.Where(s => s.Version == v).ToList() : all.ToList();

        var defVersion = version is { } rv
            ? await getVersion.ExecuteAsync(slug, rv, ct)
            : await getPublished.ExecuteAsync(slug, ct);
        var def = defVersion?.Definition.Localize(null)
            ?? throw new NotFoundException(ErrorCodes.FormNotFound, ErrorMessages.FormNotPublished, AppException.Args("slug", slug));

        // AC 1/2/4: the chosen period, clamped to what the retention allows, drives every figure below.
        var retentionDays = options.Value.RetentionDays;
        var available = AnalyticsPeriods.Offered(retentionDays);
        var applied = period.Clamp(retentionDays);
        var today = DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);
        var (from, to) = applied.Window(today);

        // Every metric is computed from the submissions within the window; an all-time figure would
        // mix periods (AC 1). The trend buckets them by the period's granularity (day, or month for the year).
        var daily = new int[applied.BucketCount()];
        var items = new List<Submission>();
        foreach (var s in byVersion)
        {
            if (applied.BucketIndex(today, DateOnly.FromDateTime(s.CreatedAt.UtcDateTime.Date)) is { } idx)
            {
                daily[idx]++;
                items.Add(s);
            }
        }

        var sources = items.Where(s => !string.IsNullOrEmpty(s.Source))
            .GroupBy(s => s.Source!).Select(g => new StatsBar(g.Key, g.Count()))
            .OrderByDescending(b => b.Count).ToList();

        QuizStats? quiz = null;
        if (def.Quiz is { } q)
        {
            var withQuiz = items.Where(s => s.Quiz is not null).ToList();
            var results = q.Results
                .Select(r => new StatsBar(r.Title.ToString(), withQuiz.Count(s => s.Quiz!.ResultId == r.Id)))
                .ToList();
            var questions = q.Questions.Select(question =>
            {
                var seen = withQuiz.Where(s => s.Quiz!.Answers.ContainsKey(question.Id)).ToList();
                var options = question.Options
                    .Select(o => new StatsBar(o.Label.ToString(), seen.Count(s => s.Quiz!.Answers[question.Id] == o.Id)))
                    .ToList();
                return new QuestionStats(question.Text.ToString(), seen.Count, options);
            }).ToList();
            quiz = new QuizStats(results,
                withQuiz.Count == 0 ? 0 : (int)Math.Round(withQuiz.Average(s => s.Quiz!.Pct)),
                withQuiz.Count(s => s.Quiz!.ReachedByJump),
                questions);
        }

        var selectFields = def.Fields
            .Where(f => f.Type is FieldTypes.Select or FieldTypes.Multiselect)
            .Select(f => new FieldStats(f.Label.ToString(),
                (f.Options ?? Array.Empty<LText>() as IReadOnlyList<LText>).Select(o =>
                {
                    var value = o.ToString();
                    var count = items.Count(s => s.Values.TryGetValue(f.Id, out var sv)
                        && Domain.Validation.FormSubmissionValidator.SplitMulti(sv).Contains(value));
                    return new StatsBar(value, count);
                }).ToList()))
            .Where(f => f.Options.Any(o => o.Count > 0))
            .ToList();

        // Funnel: view and start counters over the very same window (AC 4 - the completion rate the page
        // derives divides submissions and starts of one period, never of two).
        var totals = await funnel.ExecuteAsync(slug, from, to, ct);
        var funnelStats = totals.Count == 0 ? null : new FunnelStats(totals.GetValueOrDefault("view"), totals.GetValueOrDefault("start"));

        return new FormStats(
            items.Count, daily, versions,
            items.Count(s => s.HasEmail),
            items.Count(s => s.IsConfirmed),
            items.Count(s => s.State == SubmissionState.AwaitingConfirmation),
            items.Count(s => s.State == SubmissionState.Failed),
            sources, quiz, selectFields,
            applied, available,
            funnelStats);
    }
}
