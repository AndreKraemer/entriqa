using System.Text.Json;
using Entriqa.Application.Ports;
using Entriqa.Domain.Forms;
using Entriqa.Domain.UseCases;

namespace Entriqa.Application.Pipeline.Steps;

/// <summary>Mail to the participant through a Brevo template, optionally with a PDF attachment or a download link from an earlier step.</summary>
public sealed class BrevoMailStep(ISendTransactionalMailPort mail, IStoreArtifactPort store, ICreateDownloadLinkPort links) : ISubmissionStep
{
    public string Key => "brevo.mail";
    public string Name => "E-Mail an Teilnehmer";
    public string Description => "Verschickt eine Brevo-Vorlage an die angegebene Adresse – optional mit Datei oder Link aus einem vorherigen Schritt.";
    public StepMode Mode => StepMode.Inline;
    public StepTestBehavior TestBehavior => StepTestBehavior.Redirected;    // #21: in a test the mail goes to the admin
    public IReadOnlyList<StepNeed> Needs => new[] { StepNeed.EmailField };
    public string ConfigSchema => """{"type":"object","required":["templateId"],"properties":{"templateId":{"type":"integer","title":"Brevo-Vorlage","format":"brevo-template","localizable":true},"attach":{"type":"string","enum":["none","download","report","reportLink"],"title":"Mitschicken","default":"none"},"linkHours":{"type":"integer","title":"Link gültig (Stunden), nur bei reportLink","default":72}}}""";
    public IReadOnlyList<MailParam> MailParams => new MailParam[]
    {
        new("firstName", "Vorname aus dem Formular (kann leer sein)"),
        new("form", "Name des Formulars"),
        new("site", "Name der Website"),
        new("fields", "alle Feldwerte mit Beschriftung (Objekt)"),
        new("downloadUrl", "zeitlich begrenzter Download-Link – nur bei Mitschicken: Download-Link/PDF-Link"),
        new("downloadValidHours", "Gültigkeit des Download-Links in Stunden – nur bei Mitschicken: Download-Link/PDF-Link"),
        new("resultTitle", "Titel des Quiz-Ergebnisses – nur bei Quiz-Formularen"),
        new("resultBody", "Text des Quiz-Ergebnisses – nur bei Quiz-Formularen"),
        new("pct", "erreichte Prozent – nur bei Quiz-Formularen"),
    };

    public IEnumerable<string> CheckConfig(JsonElement config, FormDefinition form, IReadOnlySet<string> producedBefore)
    {
        if (config.GetInt("templateId", form.DefaultLocale) is null) yield return "keine Brevo-Vorlage gewählt.";
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
        // In a test the producing step (reportingcloud.pdf) is suppressed, so no "report" artifact exists.
        // Send the mail without the attachment rather than throwing a false "Failed" (#21) - the producer is
        // shown as skipped in its own protocol row. In a real run CheckConfig guarantees the artifact is present.
        var attach = config.GetString("attach") ?? "none";
        if (ctx.Test is not null && attach is "report" or "reportLink" && !ctx.Artifacts.ContainsKey("report")) attach = "none";
        switch (attach)
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

        var to = ctx.Test?.MailTo ?? ctx.Email!;                            // #21: in a test the mail goes to the admin
        await mail.SendAsync(to, ctx.FirstName, config.GetInt("templateId", ctx.Submission.Locale)!.Value, parameters, attachment, ct);
        return StepResult.Ok;
    }
}
