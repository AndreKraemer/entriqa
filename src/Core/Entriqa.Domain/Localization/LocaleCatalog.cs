namespace Entriqa.Domain.Localization;

/// <summary>
/// Which locales of a message catalog are usable. A catalog is a locale -> (key -> text) map;
/// a locale counts as complete when it has a text for every key the reference locale defines.
/// Deriving the set this way is what lets a new language become available by adding its texts
/// alone - no second list to maintain in the options or the admin.
/// </summary>
public static class LocaleCatalog
{
    public static IReadOnlyList<string> CompleteLocales(
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> byLocale, string reference) =>
        throw new NotImplementedException();
}
