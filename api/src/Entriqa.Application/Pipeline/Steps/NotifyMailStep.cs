using System.Text.Json;
using Entriqa.Application.Ports;
using Entriqa.Domain.Forms;
using Entriqa.Domain.UseCases;

namespace Entriqa.Application.Pipeline.Steps;

/// <summary>Internal notification: the submission as a Brevo transactional mail to one or more addresses.</summary>
public sealed class NotifyMailStep(ISendTransactionalMailPort mail) : ISubmissionStep
{
    public string Key => "notify.mail";
    public string Name => "Benachrichtigung an uns";
    public string Description => "Schickt die Einsendung per Brevo-Transaktionsmail an eine interne Adresse.";
    public StepMode Mode => StepMode.Inline;
    public bool CriticalByDefault => false;         // notification: a failure does not block the pipeline
    public string ConfigSchema => """{"type":"object","required":["to","templateId"],"properties":{"to":{"type":"string","title":"E-Mail-Adresse(n), durch Komma getrennt"},"templateId":{"type":"integer","title":"Brevo-Vorlage","format":"brevo-template"}}}""";
    public IReadOnlyList<MailParam> MailParams => new MailParam[]
    {
        new("form", "Name des Formulars"),
        new("slug", "Kurzname (Slug) des Formulars"),
        new("submissionId", "ID der Einsendung"),
        new("receivedAt", "Eingangszeitpunkt (dd.MM.yyyy HH:mm)"),
        new("source", "Quelle/Seite der Einsendung"),
        new("fields", "Liste der Feldwerte: in einer Schleife über params.fields als item.label / item.value"),
        new("replyTo", "E-Mail-Adresse des Einsenders (für Antworten)"),
        new("adminUrl", "Direktlink zur Einsendung im Admin"),
    };

    public IEnumerable<string> CheckConfig(JsonElement config, FormDefinition form, IReadOnlySet<string> producedBefore)
    {
        if (string.IsNullOrWhiteSpace(config.GetString("to"))) yield return "keine Empfängeradresse.";
        if (config.GetInt("templateId") is null) yield return "keine Brevo-Vorlage gewählt.";
    }

    public async Task<StepResult> ExecuteAsync(StepContext ctx, JsonElement config, CancellationToken ct)
    {
        var templateId = config.GetInt("templateId")!.Value;
        var parameters = new Dictionary<string, object?>
        {
            ["form"] = ctx.Form.Name,
            ["slug"] = ctx.Form.Slug,
            ["submissionId"] = ctx.Submission.Id,
            ["receivedAt"] = ctx.Submission.CreatedAt.ToString("dd.MM.yyyy HH:mm"),
            ["source"] = ctx.Submission.Source,
            ["fields"] = ctx.LabeledValues().Select(kv => new { label = kv.Key, value = kv.Value }).ToList(),
            ["replyTo"] = ctx.Email,
            ["adminUrl"] = $"{ctx.Options.BaseUrl.TrimEnd('/')}/admin/einsendung/{ctx.Submission.Id}",
        };
        foreach (var to in config.GetString("to")!.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            await mail.SendAsync(to, null, templateId, parameters, null, ct);
        return StepResult.Ok;
    }
}
