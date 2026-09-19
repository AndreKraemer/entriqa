namespace Entriqa.Admin.Services;

/// <summary>
/// The inline SVG glyphs the admin ships, keyed by name. Hand-authored Lucide paths rather than a
/// NuGet package: the admin is a WASM download that loads in the operator's browser, so only the
/// icons actually used may travel with it (#18, AC 2), and nothing is ever fetched at runtime
/// (#18, AC 3). Lucide is ISC-licensed; the licence text ships with the repository.
/// </summary>
public static class IconLibrary
{
    /// <summary>The names of every glyph that ships.</summary>
    public static IReadOnlyCollection<string> Names() => throw new NotImplementedException();

    /// <summary>
    /// The inner SVG markup (the <c>&lt;path&gt;</c> data) for a glyph. Fails rather than render an
    /// empty icon when no glyph of that name ships, so a typo cannot pass unnoticed.
    /// </summary>
    public static string Path(string name) => throw new NotImplementedException();
}
