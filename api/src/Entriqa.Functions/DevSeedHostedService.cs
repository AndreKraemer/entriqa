using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Entriqa.Application.Ports;
using Entriqa.Domain.Forms;

namespace Entriqa.Functions;

/// <summary>
/// Nur Entwicklung: veröffentlicht die Beispiel-Definitionen aus seed/forms/*.json, wenn das Formular noch nicht existiert.
/// In Produktion kommt alles aus dem Admin. (Kein Schema-Setup beim Start in Prod – Solution Standard §14.)
/// </summary>
public sealed class DevSeedHostedService(
    IHostEnvironment env,
    IConfiguration config,
    ITryGetPublishedFormQuery getPublished,
    IPublishFormVersionCommand publish,
    ILogger<DevSeedHostedService> log) : IHostedService
{
    public async Task StartAsync(CancellationToken ct)
    {
        var folder = config["Entriqa:SeedFolder"];
        if (!env.IsDevelopment() || string.IsNullOrEmpty(folder)) return;
        var path = ResolveSeedFolder(folder);
        if (path is null) { log.LogWarning("Seed-Ordner {Folder} nicht gefunden (gesucht ab {Base} aufwärts)", folder, AppContext.BaseDirectory); return; }

        var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        foreach (var file in Directory.EnumerateFiles(path, "*.json"))
        {
            try
            {
                var def = JsonSerializer.Deserialize<FormDefinition>(await File.ReadAllTextAsync(file, ct), json);
                if (def is null || await getPublished.ExecuteAsync(def.Slug, ct) is not null) continue;
                await publish.ExecuteAsync(def, "seed", ct);
                log.LogInformation("Seed: {Slug} veröffentlicht", def.Slug);
            }
            catch (Exception ex) { log.LogWarning(ex, "Seed {File} übersprungen", file); }
        }
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;

    /// <summary>Relative Pfade ab dem Ausgabeverzeichnis aufwärts suchen – funktioniert für func start, dotnet run und Tests gleichermaßen.</summary>
    private static string? ResolveSeedFolder(string folder)
    {
        if (Path.IsPathRooted(folder)) return Directory.Exists(folder) ? folder : null;
        var relative = folder.Replace("../", "", StringComparison.Ordinal).Replace("..\\", "", StringComparison.Ordinal);
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, relative);
            if (Directory.Exists(candidate)) return candidate;
        }
        return null;
    }
}
