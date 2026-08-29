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
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> byLocale, string reference)
    {
        // Without the reference locale there is nothing to be complete against, so nothing qualifies -
        // better an empty set the caller notices than a set measured against an arbitrary locale.
        if (!byLocale.TryGetValue(reference, out var required)) return Array.Empty<string>();
        return byLocale
            .Where(entry => required.Keys.All(entry.Value.ContainsKey))
            .Select(entry => entry.Key)
            .ToList();
    }
}
