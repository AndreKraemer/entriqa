namespace Entriqa.Application;

/// <summary>Alles, was pro Website anders ist. Kommt aus App-Settings (Entriqa__Brevo__ApiKey usw.), nie aus dem Repo.</summary>
public sealed class EntriqaOptions
{
    public const string Section = "Entriqa";

    public string SiteName { get; set; } = "Website";
    public string BaseUrl { get; set; } = "https://example.org";            // öffentliche Basis-URL der Hugo-Seite (SWA: gleiche Origin wie /api)
    public string TokenSecret { get; set; } = "";                           // mind. 32 zufällige Zeichen
    public int MinSubmitSeconds { get; set; } = 3;
    public int MaxSubmitHours { get; set; } = 2;
    public int RateLimitPerWindow { get; set; } = 5;
    public int RateLimitWindowMinutes { get; set; } = 10;
    public int ConfirmTokenDays { get; set; } = 14;
    public string HousekeepingKey { get; set; } = "";                       // Secret-Header x-housekeeping-key; leer = Endpunkt deaktiviert
    public int RetentionDays { get; set; } = 180;                           // Einsendungen (samt Blobs) danach löschen
    public int UnconfirmedRetentionDays { get; set; } = 14;                 // unbestätigte DOI-Einsendungen danach löschen
    public int SweepAfterMinutes { get; set; } = 10;                        // liegengebliebene Deferred-Läufe erst nach dieser Schonfrist nachziehen
    public int AutoRetryMax { get; set; } = 3;                              // fehlgeschlagene Schritte höchstens so oft automatisch wiederholen
    public string ConfirmPagePath { get; set; } = "/bestaetigen/";          // statische Seite mit POST-Button (nie GET-Bestätigung: Link-Scanner!)
    public string ConfirmedRedirectPath { get; set; } = "/bestaetigt/";
    public string IpHashSalt { get; set; } = "";                            // täglich rotierender Salt wäre besser; siehe Spec
    public string FreemailBlocklist { get; set; } = "";                     // zusätzliche Freemail-Domains (kommasepariert), ergänzt FreemailDomains.Default
    public string Locales { get; set; } = "de,en";                          // Sprachen der Website (kommasepariert) – der Admin bietet genau diese an
    public string LicenseKey { get; set; } = "";                            // Entriqa-Produktivlizenz (leer = Entwicklung, Admin zeigt Hinweis)

    private IReadOnlyList<string>? _siteLocales;
    public IReadOnlyList<string> SiteLocales =>
        _siteLocales ??= Locales.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.ToLowerInvariant()).Distinct().ToList() is { Count: > 0 } list ? list : new List<string> { "de" };

    private IReadOnlySet<string>? _extraFreemail;
    public IReadOnlySet<string> ExtraFreemailDomains =>
        _extraFreemail ??= FreemailBlocklist.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    public bool AllowAnonymousAdmin { get; set; }                           // nur lokal ohne SWA-Auth
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
    public string PrivateContainer { get; set; } = "forms-private";          // leadmagnets/… und reports/…
}
