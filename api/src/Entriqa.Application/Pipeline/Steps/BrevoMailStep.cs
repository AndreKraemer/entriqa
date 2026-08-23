using System.Text.Json;
using Entriqa.Application.Ports;
using Entriqa.Domain.Forms;

namespace Entriqa.Application.Pipeline.Steps;

/// <summary>Mail an den Teilnehmer über eine Brevo-Vorlage, optional mit PDF-Anhang oder Download-Link aus einem vorherigen Schritt.</summary>
public sealed class BrevoMailStep(ISendTransactionalMailPort mail, IStoreArtifactPort store, ICreateDownloadLinkPort links) : ISubmissionStep
{
    public string Key => "brevo.mail";
    public string Name => "E-Mail an Teilnehmer";
    public string Description => "Verschickt eine Brevo-Vorlage an die angegebene Adresse – optional mit Datei oder Link aus einem vorherigen Schritt.";
    public StepMode Mode => StepMode.Inline;
    public IReadOnlyList<StepNeed> Needs => new[] { StepNeed.EmailField };
    public string ConfigSchema => """{"type":"object","required":["templateId"],"properties":{"templateId":{"type":"integer","title":"Brevo-Vorlage","format":"brevo-template"},"attach":{"type":"string","enum":["none","download","report","reportLink"],"title":"Mitschicken","default":"none"},"linkHours":{"type":"integer","title":"Link gültig (Stunden), nur bei reportLink","default":72}}}""";

    public IEnumerable<string> CheckConfig(JsonElement config, FormDefinition form, IReadOnlySet<string> producedBefore)
    {
        if (config.GetInt("templateId") is null) yield return "keine Brevo-Vorlage gewählt.";
        var attach = config.GetString("attach") ?? "none";
        if (attach == "download" && !producedBefore.Contains("download")) yield return "\"Download-Link\" wird von keinem vorherigen Schritt erzeugt.";
        if (attach is "report" or "reportLink" && !producedBefore.Contains("report")) yield return "\"PDF\" wird von keinem vorherigen Schritt erzeugt.";
    }

    public async Task<StepResult> ExecuteAsync(StepContext ctx, JsonElement config, CancellationToken ct)
    {
        var parameters = new Dictionary<string, object?>
        {
            ["firstName"] = ctx.FirstName,
            ["form"] = ctx.Form.Name,
            ["site"] = ctx.Options.SiteName,
            ["fields"] = ctx.LabeledValues(),
        };
        if (ctx.QuizResult is { } r && ctx.Submission.Quiz is { } q)
        {
            parameters["resultTitle"] = r.Title; parameters["resultBody"] = r.Body; parameters["pct"] = q.Pct;
        }

        MailAttachment? attachment = null;
        switch (config.GetString("attach") ?? "none")
        {
            case "download":
                parameters["downloadUrl"] = ctx.Artifacts.GetValueOrDefault("download");
                parameters["downloadValidHours"] = ctx.Artifacts.GetValueOrDefault("downloadValidHours");
                break;
            case "report":
                var path = ctx.Artifacts["report"];
                attachment = new MailAttachment($"{ctx.Form.Slug}-ergebnis.pdf", await store.ReadAsync(path, ct));
                break;
            case "reportLink":
                var hours = config.GetInt("linkHours") ?? 72;
                parameters["downloadUrl"] = (await links.CreateAsync(ctx.Artifacts["report"], TimeSpan.FromHours(hours), ct)).ToString();
                parameters["downloadValidHours"] = hours;
                break;
        }

        await mail.SendAsync(ctx.Email!, ctx.FirstName, config.GetInt("templateId")!.Value, parameters, attachment, ct);
        return StepResult.Ok;
    }
}
