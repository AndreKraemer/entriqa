using Entriqa.Application.Ports;
using Entriqa.Domain.UseCases;

namespace Entriqa.Application.UseCases;

/// <summary>
/// Drop-off analytics without personal data: counts "view" (form rendered) and "start" (first input)
/// as daily totals. Together with the submission count that gives the drop-off rate - nothing more is
/// collected on purpose (no ids, no partial input, no cookies). Unknown slugs and types are dropped
/// silently so that the endpoint cannot litter a table.
/// </summary>
internal sealed class CountFormEventUseCase(
    ITryGetPublishedFormQuery getPublished,
    IIncrementFunnelCommand increment,
    TimeProvider time) : ICountFormEventUseCase
{
    private static readonly HashSet<string> Types = new() { "view", "start" };

    public async Task ExecuteAsync(string slug, string type, CancellationToken ct = default)
    {
        if (!Types.Contains(type)) return;
        if (await getPublished.ExecuteAsync(slug, ct) is null) return;
        await increment.ExecuteAsync(slug, DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime), type, ct);
    }
}
