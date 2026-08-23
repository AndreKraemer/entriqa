using Entriqa.Application.Ports;
using Entriqa.Domain.UseCases;

namespace Entriqa.Application.UseCases;

/// <summary>
/// Abbruch-Analytics ohne Personenbezug: zählt "view" (Formular gerendert) und "start" (erste Eingabe)
/// als Tages-Summen. Zusammen mit der Einsendungszahl ergibt das die Abbruchquote – mehr wird bewusst
/// nicht erhoben (keine IDs, keine Teil-Eingaben, keine Cookies). Unbekannte Slugs/Typen werden still
/// verworfen, damit der Endpunkt keine Tabelle zumüllen kann.
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
