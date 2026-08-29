using System.Globalization;
using Microsoft.Extensions.Options;
using Entriqa.Application;
using Entriqa.Application.Ports;

namespace Entriqa.Infrastructure.Dev;

/// <summary>
/// Local development without a Brevo key: a synthetic directory, so the step editor can be driven at all.
/// Active only while <c>Entriqa__Brevo__ApiKey</c> is empty and <c>Entriqa__Dev__BrevoDirectorySize</c> is set.
/// The names vary on purpose - a directory of "Liste 1..120" would make the search look like it works
/// while it only ever matches everything. <c>Entriqa__Dev__BrevoDirectoryFailure</c> lets one section fail,
/// which is the only way to see what an incompletely loaded directory looks like without breaking Brevo.
/// </summary>
public sealed class DevBrevoDirectoryAdapter(IOptions<EntriqaOptions> options) : IBrevoDirectoryPort
{
    private static readonly string[] ListThemes =
        { "Newsletter", "Kunden", "Interessenten", "Webinar", "Whitepaper", "Beratung", "Partner", "Intern" };
    private static readonly string[] TemplateThemes =
        { "Bestätigung", "Willkommen", "Ergebnis", "Erinnerung", "Download", "Einladung" };

    public Task<bool> IsReachableAsync(CancellationToken ct = default) => Task.FromResult(true);

    public Task<IReadOnlyList<BrevoListInfo>> GetListsAsync(CancellationToken ct = default)
    {
        FailIfConfigured("lists");
        return Task.FromResult<IReadOnlyList<BrevoListInfo>>(
            Entries(ListThemes).Select(e => new BrevoListInfo(e.Id, e.Name)).ToList());
    }

    public Task<IReadOnlyList<BrevoTemplateInfo>> GetTemplatesAsync(CancellationToken ct = default)
    {
        FailIfConfigured("templates");
        return Task.FromResult<IReadOnlyList<BrevoTemplateInfo>>(
            Entries(TemplateThemes).Select(e => new BrevoTemplateInfo(e.Id, e.Name)).ToList());
    }

    private IEnumerable<(long Id, string Name)> Entries(string[] themes) =>
        Enumerable.Range(1, options.Value.Dev.BrevoDirectorySize)
            .Select(i => ((long)i, $"{themes[(i - 1) % themes.Length]} {i.ToString(CultureInfo.InvariantCulture)}"));

    private void FailIfConfigured(string section)
    {
        if (string.Equals(options.Value.Dev.BrevoDirectoryFailure, section, StringComparison.OrdinalIgnoreCase))
            throw new HttpRequestException($"Dev directory: '{section}' fails on purpose.");
    }
}
