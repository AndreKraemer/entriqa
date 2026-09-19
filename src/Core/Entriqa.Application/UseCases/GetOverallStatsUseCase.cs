using Entriqa.Application.Ports;
using Entriqa.Domain.UseCases;

namespace Entriqa.Application.UseCases;

/// <summary>
/// Statistics across all published forms in one pass (#16) - the view when no single form is chosen.
/// A separate use case from <see cref="GetFormStatsUseCase"/> on purpose: it needs no form definition
/// (there is none across forms) and deliberately carries no quiz metrics.
/// </summary>
internal sealed class GetOverallStatsUseCase(
    IListAllSubmissionsForStatsQuery list,
    IListFormsQuery forms,
    IGetAllFunnelTotalsQuery funnel,
    TimeProvider time) : IGetOverallStatsUseCase
{
    public Task<OverallStats> ExecuteAsync(CancellationToken ct = default)
    {
        // Red-state skeleton (#16): no logic yet. Returns an empty result so the value-asserting tests
        // fail on their assertions rather than on a missing member. The implementation follows in
        // /pairmode:implement.
        _ = (list, forms, funnel, time);
        return Task.FromResult(new OverallStats(0, new int[14], 0, 0, 0, 0,
            Array.Empty<StatsBar>(), Array.Empty<FormBreakdown>()));
    }
}
