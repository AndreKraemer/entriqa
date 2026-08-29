using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Entriqa.Application;

namespace Entriqa.Functions;

/// <summary>
/// Names the configured languages Entriqa cannot serve completely. Without this the mistake is
/// invisible to everyone who could fix it: a code in Entriqa__Locales that has no visitor-facing
/// texts is dropped silently, and only the visitor ever sees the result - a translated form with
/// German buttons.
/// </summary>
public sealed class LocaleWarningHostedService(
    IOptions<EntriqaOptions> options,
    ILogger<LocaleWarningHostedService> log) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        foreach (var locale in options.Value.UnsupportedLocales)
            log.LogWarning(
                "Entriqa__Locales enthält '{Locale}', wofür keine vollständigen besucherseitigen Texte vorliegen. " +
                "Diese Sprache wird nicht angeboten.", locale);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
