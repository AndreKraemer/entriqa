namespace Entriqa.Application.Localization;

/// <summary>
/// Which language an answer is written in. Explicit beats implicit: <c>?lang=</c> is what the caller asked
/// for (forms.js passes the data-lang of the embedding page, the admin passes its interface language),
/// Accept-Language is the browser's guess, and the first configured site locale has the last word.
/// Anything not configured is ignored, so an unknown language never reaches the message catalog.
/// Pure string handling on purpose - the HTTP details stay at the edge (RequestLocale).
/// </summary>
public static class LocaleNegotiation
{
    public static string Pick(string? explicitLang, string? acceptLanguage, IReadOnlyList<string> siteLocales)
    {
        var fallback = siteLocales.Count > 0 ? siteLocales[0] : "de";
        if (Match(explicitLang, siteLocales) is { } asked) return asked;
        foreach (var tag in ByQuality(acceptLanguage))
            if (Match(tag, siteLocales) is { } offered) return offered;
        return fallback;
    }

    private static string? Match(string? lang, IReadOnlyList<string> siteLocales)
    {
        if (string.IsNullOrWhiteSpace(lang)) return null;
        var bare = lang.Split('-')[0].Trim();
        return siteLocales.FirstOrDefault(l => string.Equals(l, bare, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Tags of an Accept-Language header, best quality first ("de-DE,de;q=0.9,en;q=0.8"). q=0 means "not this one".</summary>
    private static IEnumerable<string> ByQuality(string? header)
    {
        if (string.IsNullOrWhiteSpace(header)) return Array.Empty<string>();
        return header.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(part =>
            {
                var pieces = part.Split(';', StringSplitOptions.TrimEntries);
                var quality = 1.0;
                foreach (var piece in pieces.Skip(1))
                    if (piece.StartsWith("q=", StringComparison.OrdinalIgnoreCase)
                        && double.TryParse(piece[2..], System.Globalization.NumberStyles.Float,
                                           System.Globalization.CultureInfo.InvariantCulture, out var q))
                        quality = q;
                return (Tag: pieces[0], Quality: quality);
            })
            .Where(x => x.Quality > 0)
            .OrderByDescending(x => x.Quality)
            .Select(x => x.Tag)
            .ToList();
    }
}
