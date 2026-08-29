using Entriqa.Domain.Errors;
using Entriqa.Domain.Validation;

namespace Entriqa.Domain.Localization;

/// <summary>
/// The languages Entriqa can serve a visitor completely. A language qualifies only when both
/// visitor-facing catalogs carry it - the field and control texts (ValidationMessages) and the
/// error texts rendered at the edge (ErrorMessages). Half a translation is worse than none:
/// the visitor is the only one who ever sees the mixture.
/// </summary>
public static class SupportedLocales
{
    /// <summary>
    /// The rule the guarantee rests on: a locale counts only when <em>every</em> catalog carries it.
    /// Separate from <see cref="All"/> so it can be checked against catalogs that actually differ - as
    /// long as the shipped ones carry the same locales, intersecting and unioning them are
    /// indistinguishable, and the difference only shows once a language is added to one of them.
    /// </summary>
    public static IReadOnlyList<string> CarriedByAll(params IReadOnlyList<string>[] catalogLocales) =>
        catalogLocales.Length == 0
            ? Array.Empty<string>()
            : catalogLocales.Aggregate((a, b) => (IReadOnlyList<string>)a.Intersect(b).ToList());

    public static IReadOnlyList<string> All { get; } =
        CarriedByAll(ValidationMessages.Locales, ErrorMessages.Locales);
}
