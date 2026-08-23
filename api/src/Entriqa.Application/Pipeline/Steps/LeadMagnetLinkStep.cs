using System.Text.Json;
using Entriqa.Application.Ports;
using Entriqa.Domain.Forms;

namespace Entriqa.Application.Pipeline.Steps;

/// <summary>Zeitlich begrenzter Download-Link auf eine privat abgelegte Datei (leadmagnets/…). Artefakt "download".</summary>
public sealed class LeadMagnetLinkStep(ICreateDownloadLinkPort links) : ISubmissionStep
{
    public string Key => "leadmagnet.link";
    public string Name => "Download-Link erzeugen";
    public string Description => "Erzeugt einen zeitlich begrenzten Link auf eine privat gespeicherte Datei.";
    public StepMode Mode => StepMode.Inline;
    public string? Produces => "download";
    public string ConfigSchema => """{"type":"object","required":["blob"],"properties":{"blob":{"type":"string","title":"Datei (Blob-Pfad unter leadmagnets/)","format":"blob"},"hours":{"type":"integer","title":"Link gültig (Stunden)","default":48}}}""";

    public IEnumerable<string> CheckConfig(JsonElement config, FormDefinition form, IReadOnlySet<string> producedBefore)
    {
        if (string.IsNullOrWhiteSpace(config.GetString("blob"))) yield return "keine Datei hochgeladen.";
    }

    public async Task<StepResult> ExecuteAsync(StepContext ctx, JsonElement config, CancellationToken ct)
    {
        var hours = config.GetInt("hours") ?? 48;
        var uri = await links.CreateAsync(config.GetString("blob")!, TimeSpan.FromHours(hours), ct);
        ctx.Artifacts["download"] = uri.ToString();
        ctx.Artifacts["downloadValidHours"] = hours.ToString();
        return StepResult.Ok;
    }
}
