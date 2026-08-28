using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Entriqa.Application.Ports;
using Entriqa.Domain.Forms;

namespace Entriqa.Functions;

/// <summary>
/// Development only: publishes the example definitions from seed/forms/*.json when the form does not exist yet,
/// and uploads the lead magnet files next to them so a sample configuring a download can actually deliver it.
/// In production everything comes from the admin. (No schema setup at startup in prod - Solution Standard §14.)
/// </summary>
public sealed class DevSeedHostedService(
    IHostEnvironment env,
    IConfiguration config,
    ITryGetPublishedFormQuery getPublished,
    IPublishFormVersionCommand publish,
    IStoreArtifactPort artifacts,
    ILogger<DevSeedHostedService> log) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var folder = config["Entriqa:SeedFolder"];
        if (!env.IsDevelopment() || string.IsNullOrEmpty(folder)) return;
        var path = ResolveSeedFolder(folder);
        if (path is null) { log.LogWarning("Seed-Ordner {Folder} nicht gefunden (gesucht ab {Base} aufwärts)", folder, AppContext.BaseDirectory); return; }

        await SeedLeadMagnetsAsync(path, cancellationToken);

        var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        foreach (var file in Directory.EnumerateFiles(path, "*.json"))
        {
            try
            {
                var def = JsonSerializer.Deserialize<FormDefinition>(await File.ReadAllTextAsync(file, cancellationToken), json);
                if (def is null || await getPublished.ExecuteAsync(def.Slug, cancellationToken) is not null) continue;
                await publish.ExecuteAsync(def, "seed", cancellationToken);
                log.LogInformation("Seed: {Slug} veröffentlicht", def.Slug);
            }
            catch (Exception ex) { log.LogWarning(ex, "Seed {File} übersprungen", file); }
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// Uploads seed/leadmagnets/* to leadmagnets/&lt;name&gt; in the private container. The forms
    /// reference those paths, and BlobArtifactAdapter fails the step when the blob is absent - so
    /// without this the shipped whitepaper example ends in a failed run on a fresh environment.
    /// </summary>
    private async Task SeedLeadMagnetsAsync(string seedPath, CancellationToken ct)
    {
        var dir = Path.Combine(Path.GetDirectoryName(seedPath.TrimEnd(Path.DirectorySeparatorChar))!, "leadmagnets");
        if (!Directory.Exists(dir)) return;
        foreach (var file in Directory.EnumerateFiles(dir))
        {
            var blobPath = "leadmagnets/" + Path.GetFileName(file);
            try
            {
                await artifacts.StoreAsync(blobPath, await File.ReadAllBytesAsync(file, ct), ContentTypeOf(file), ct);
                log.LogInformation("Seed: {Blob} hochgeladen", blobPath);
            }
            catch (Exception ex) { log.LogWarning(ex, "Seed-Datei {File} übersprungen", file); }
        }
    }

    private static string ContentTypeOf(string file) => Path.GetExtension(file).ToLowerInvariant() switch
    {
        ".pdf" => "application/pdf",
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        _ => "application/octet-stream",
    };

    /// <summary>Search relative paths upwards from the output directory - works for func start, dotnet run and tests alike.</summary>
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
