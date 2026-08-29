namespace Entriqa.Domain.Localization;

/// <summary>
/// The languages Entriqa can serve a visitor completely. A language qualifies only when both
/// visitor-facing catalogs carry it - the field and control texts (ValidationMessages) and the
/// error texts rendered at the edge (ErrorMessages). Half a translation is worse than none:
/// the visitor is the only one who ever sees the mixture.
/// </summary>
public static class SupportedLocales
{
    public static IReadOnlyList<string> All => throw new NotImplementedException();
}
