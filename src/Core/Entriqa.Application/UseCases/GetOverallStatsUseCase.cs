using Entriqa.Application.Ports;
using Entriqa.Domain.Submissions;
using Entriqa.Domain.UseCases;

namespace Entriqa.Application.UseCases;

/// <summary>
/// Statistics across all published forms in one pass (#16) - the view when no single form is chosen.
/// A separate use case from <see cref="GetFormStatsUseCase"/> on purpose: it needs no form definition
/// (there is none across forms) and deliberately carries no quiz metrics. Only forms that are currently
/// published contribute, so submissions of an unpublished slug are left out of every figure.
/// Totals are all-time; the daily trend and the funnel (views, starts) cover the same 14 days as the
/// per-form view, and per form the completion rate divides those 14-day submissions by the starts.
/// </summary>
internal sealed class GetOverallStatsUseCase(
    IListAllSubmissionsForStatsQuery list,
    IListFormsQuery forms,
    IGetAllFunnelTotalsQuery funnel,
    TimeProvider time) : IGetOverallStatsUseCase
{
    public async Task<OverallStats> ExecuteAsync(CancellationToken ct = default)
    {
        var published = (await forms.ExecuteAsync(ct)).Where(f => f.Status == "published").ToList();
        var publishedSlugs = published.Select(f => f.Slug).ToHashSet(StringComparer.Ordinal);

        var all = await list.ExecuteAsync(5000, ct);
        var items = all.Where(s => publishedSlugs.Contains(s.Slug)).ToList();

        var today = time.GetUtcNow().UtcDateTime.Date;
        var daily = new int[14];
        foreach (var s in items)
        {
            var idx = 13 - (int)(today - s.CreatedAt.UtcDateTime.Date).TotalDays;
            if (idx is >= 0 and <= 13) daily[idx]++;
        }

        var sources = items.Where(s => !string.IsNullOrEmpty(s.Source))
            .GroupBy(s => s.Source!).Select(g => new StatsBar(g.Key, g.Count()))
            .OrderByDescending(b => b.Count).ToList();

        var funnelTotals = await funnel.ExecuteAsync(DateOnly.FromDateTime(today).AddDays(-13), ct);
        var bySlug = items.GroupBy(s => s.Slug).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
        var breakdown = published.Select(f =>
        {
            var subs = bySlug.GetValueOrDefault(f.Slug) ?? new List<Submission>();
            var recent = subs.Count(s => (today - s.CreatedAt.UtcDateTime.Date).TotalDays is >= 0 and <= 13);
            var fu = funnelTotals.GetValueOrDefault(f.Slug);
            return new FormBreakdown(f.Slug, f.Name, subs.Count, recent, fu?.Views ?? 0, fu?.Starts ?? 0);
        }).OrderByDescending(f => f.Total).ThenBy(f => f.Name, StringComparer.Ordinal).ToList();

        return new OverallStats(
            items.Count, daily,
            items.Count(s => s.HasEmail),
            items.Count(s => s.IsConfirmed),
            items.Count(s => s.State == SubmissionState.AwaitingConfirmation),
            items.Count(s => s.State == SubmissionState.Failed),
            sources, breakdown);
    }
}
