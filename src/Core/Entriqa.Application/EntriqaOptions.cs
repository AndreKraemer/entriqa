namespace Entriqa.Application;

/// <summary>Everything that differs per website. Comes from app settings (Entriqa__Brevo__ApiKey and friends), never from the repository.</summary>
public sealed class EntriqaOptions
{
    public const string Section = "Entriqa";

    public string SiteName { get; set; } = "Website";
    public string BaseUrl { get; set; } = "https://example.org";            // public base URL of the Hugo site (SWA: same origin as /api)
    public string TokenSecret { get; set; } = "";                           // at least 32 random characters
    public int MinSubmitSeconds { get; set; } = 3;
    public int MaxSubmitHours { get; set; } = 2;
    public int RateLimitPerWindow { get; set; } = 5;
    public int RateLimitWindowMinutes { get; set; } = 10;
    public int ConfirmTokenDays { get; set; } = 14;
    public string HousekeepingKey { get; set; } = "";                       // Secret-Header x-housekeeping-key; leer = Endpunkt deaktiviert
    public int RetentionDays { get; set; } = 180;                           // delete submissions (blobs included) after this many days
    public int UnconfirmedRetentionDays { get; set; } = 14;                 // delete unconfirmed DOI submissions after this many days
    public int SweepAfterMinutes { get; set; } = 10;                        // only pick up stalled deferred runs after this grace period
    public int AutoRetryMax { get; set; } = 3;                              // retry a failed step automatically at most this many times
    public string ConfirmPagePath { get; set; } = "/bestaetigen/";          // static page with a POST button (never a GET confirmation: link scanners!)
    public string ConfirmedRedirectPath { get; set; } = "/bestaetigt/";
    // Real URL per language, e.g. Entriqa__ConfirmPagePaths__en=/en/confirm/. A site names its own
    // paths because a translated page has a translated slug - prefixing the German one would produce
    // /en/bestaetigen/, which is nobody's URL.
    public Dictionary<string, string> ConfirmPagePaths { get; set; } = new();
    public Dictionary<string, string> ConfirmedRedirectPaths { get; set; } = new();
    public string IpHashSalt { get; set; } = "";                            // a salt rotating daily would be better; see the spec
    public string FreemailBlocklist { get; set; } = "";                     // additional freemail domains (comma separated), extends FreemailDomains.Default
    public string Locales { get; set; } = "de,en";                          // languages of the website (comma separated) - the admin offers exactly these
    public string LicenseKey { get; set; } = "";                            // Entriqa production license (empty = development, the admin shows a notice)

    private IReadOnlyList<string>? _siteLocales;
    public IReadOnlyList<string> SiteLocales =>
        _siteLocales ??= Locales.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.ToLowerInvariant()).Distinct().ToList() is { Count: > 0 } list ? list : new List<string> { "de" };

    /// <summary>
    /// The confirmation page for a submission's language. Without this the participant who filled
    /// in the English form still landed on the German page. Resolution order: the path configured
    /// for that language, otherwise Hugo's default layout as a fallback - the first locale at the
    /// root, every other one under /{locale}/. The fallback keeps the language right even when the
    /// path reads oddly; an unknown or missing locale stays at the root.
    /// </summary>
    public string ConfirmPagePathFor(string? locale) => LocalizedPath(ConfirmPagePath, ConfirmPagePaths, locale);

    /// <summary>Where the confirmation redirects to afterwards - resolved the same way.</summary>
    public string ConfirmedRedirectPathFor(string? locale) => LocalizedPath(ConfirmedRedirectPath, ConfirmedRedirectPaths, locale);

    private string LocalizedPath(string fallback, Dictionary<string, string> byLocale, string? locale)
    {
        if (locale is null) return fallback;
        var l = locale.ToLowerInvariant();
        // App-setting keys arrive in whatever casing the site wrote them, so match case-insensitively.
        foreach (var (key, path) in byLocale)
            if (string.Equals(key, l, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(path))
                return path;
        var locales = SiteLocales;
        return l == locales[0] || !locales.Contains(l) ? fallback : $"/{l}{fallback}";
    }

    private IReadOnlySet<string>? _extraFreemail;
    public IReadOnlySet<string> ExtraFreemailDomains =>
        _extraFreemail ??= FreemailBlocklist.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    public bool AllowAnonymousAdmin { get; set; }                           // local only, without SWA auth
    public BrevoOptions Brevo { get; set; } = new();
    public ReportingCloudOptions ReportingCloud { get; set; } = new();
    public StorageOptions Storage { get; set; } = new();
}

public sealed class BrevoOptions
{
    public string ApiKey { get; set; } = "";
    public string SenderName { get; set; } = "";
    public string SenderEmail { get; set; } = "";
    public string BaseUrl { get; set; } = "https://api.brevo.com/v3/";
}

public sealed class ReportingCloudOptions
{
    public string ApiKey { get; set; } = "";
    public string BaseUrl { get; set; } = "https://api.reporting.cloud/v1/";
}

public sealed class StorageOptions
{
    public string ConnectionString { get; set; } = "UseDevelopmentStorage=true";
    public string TablePrefix { get; set; } = "forms";                       // formsForms, formsVersions, formsSubmissions, formsNonces, formsRateLimits
    public string PrivateContainer { get; set; } = "forms-private";          // leadmagnets/… and reports/…
}
