namespace Entriqa.Admin.Services;

/// <summary>
/// The inline SVG glyphs the admin ships, keyed by name. Hand-authored Lucide paths rather than a
/// NuGet package: the admin is a WASM download that loads in the operator's browser, so only the
/// icons actually used may travel with it (#18, AC 2), and nothing is ever fetched at runtime
/// (#18, AC 3). Lucide is ISC-licensed; the licence text ships with the repository.
/// </summary>
public static class IconLibrary
{
    // The inner markup of each 24x24 Lucide glyph (ISC, see LUCIDE-LICENSE.txt). The component wraps
    // it in the shared <svg>. Only the glyphs the admin actually references live here - a source guard
    // (IconLibraryTests) keeps this set equal to what the .razor files use, so nothing unused travels
    // in the WASM download (#18, AC 2).
    private static readonly Dictionary<string, string> Glyphs = new(StringComparer.Ordinal)
    {
        ["x"] = """<path d="M18 6 6 18"/><path d="m6 6 12 12"/>""",
        ["chevron-up"] = """<path d="m18 15-6-6-6 6"/>""",
        ["chevron-down"] = """<path d="m6 9 6 6 6-6"/>""",
        ["pencil"] = """<path d="M21.174 6.812a1 1 0 0 0-3.986-3.987L3.842 16.174a2 2 0 0 0-.5.83l-1.321 4.352a.5.5 0 0 0 .623.622l4.353-1.32a2 2 0 0 0 .83-.497z"/><path d="m15 5 4 4"/>""",
        ["inbox"] = """<path d="M22 12h-6l-2 3h-4l-2-3H2"/><path d="M5.45 5.11 2 12v6a2 2 0 0 0 2 2h16a2 2 0 0 0 2-2v-6l-3.45-6.89A2 2 0 0 0 16.76 4H7.24a2 2 0 0 0-1.79 1.11z"/>""",
        ["code"] = """<path d="m16 18 6-6-6-6"/><path d="m8 6-6 6 6 6"/>""",
    };

    /// <summary>The names of every glyph that ships.</summary>
    public static IReadOnlyCollection<string> Names() => [.. Glyphs.Keys];

    /// <summary>
    /// The inner SVG markup (the <c>&lt;path&gt;</c> data) for a glyph. Fails rather than render an
    /// empty icon when no glyph of that name ships, so a typo cannot pass unnoticed.
    /// </summary>
    public static string Path(string name) =>
        Glyphs.TryGetValue(name, out var markup)
            ? markup
            : throw new KeyNotFoundException($"No icon named '{name}' ships with the admin.");
}
