using System.Text.Json;
using Entriqa.Application.Ports;
using Entriqa.Domain.Forms;

namespace Entriqa.Application.Pipeline.Steps;

/// <summary>
/// PDF über TX Text Control ReportingCloud. Bei Quizzes eine Vorlage je Ergebnis, sonst eine Vorlage.
/// Läuft im Hintergrund (Deferred), legt das PDF unter reports/ ab und das Artefakt "report" in den Kontext.
/// </summary>
public sealed class ReportingCloudPdfStep(IMergeDocumentPort merge, IStoreArtifactPort store, TimeProvider time) : ISubmissionStep
{
    public string Key => "reportingcloud.pdf";
    public string Name => "PDF erzeugen (ReportingCloud)";
    public string Description => "Füllt eine Word-Vorlage mit den Angaben und erzeugt ein PDF. Läuft im Hintergrund.";
    public StepMode Mode => StepMode.Deferred;
    public string? Produces => "report";
    public string ConfigSchema => """{"type":"object","properties":{"template":{"type":"string","title":"Vorlage (ohne Quiz)","format":"rc-template"},"templates":{"type":"object","title":"Vorlage je Quiz-Ergebnis","additionalProperties":{"type":"string","format":"rc-template"}}}}""";

    public IEnumerable<string> CheckConfig(JsonElement config, FormDefinition form, IReadOnlySet<string> producedBefore)
    {
        if (form.Quiz is not null)
        {
            var map = config.GetStringMap("templates");
            foreach (var r in form.Quiz.Results.Where(r => !map.ContainsKey(r.Id)))
                yield return $"Vorlage fehlt für Ergebnis '{r.Title}'.";
        }
        else if (string.IsNullOrWhiteSpace(config.GetString("template"))) yield return "keine Vorlage gewählt.";
    }

    public async Task<StepResult> ExecuteAsync(StepContext ctx, JsonElement config, CancellationToken ct)
    {
        var template = ctx.Form.Quiz is not null
            ? config.GetStringMap("templates").GetValueOrDefault(ctx.Submission.Quiz!.ResultId)
            : config.GetString("template");
        if (string.IsNullOrWhiteSpace(template)) return StepResult.Failed("Keine Vorlage für dieses Ergebnis konfiguriert.");

        var quiz = ctx.Submission.Quiz;
        var mergeData = new Dictionary<string, object?>
        {
            ["Vorname"] = ctx.FirstName ?? "",
            ["Email"] = ctx.Email,
            ["Datum"] = time.GetUtcNow().ToString("dd.MM.yyyy"),
            ["Formular"] = ctx.Form.Name,
            ["QuizVersion"] = ctx.FormVersion,
            ["Felder"] = ctx.LabeledValues().Select(kv => new { Label = kv.Key, Wert = kv.Value }).ToList(),
        };
        if (quiz is not null && ctx.QuizResult is { } result)
        {
            mergeData["Ergebnis"] = new { result.Title, Text = result.Body };
            mergeData["Punkte"] = quiz.Points;
            mergeData["MaxPunkte"] = quiz.MaxPoints;
            mergeData["Prozent"] = quiz.Pct;
            mergeData["Antworten"] = quiz.Path.Select(qid =>
            {
                var q = ctx.Form.Quiz!.Questions.First(x => x.Id == qid);
                var o = q.Options.First(x => x.Id == quiz.Answers[qid]);
                return new { Frage = q.Text, Antwort = o.Label, Punkte = o.Points };
            }).ToList();
        }

        var pdf = await merge.MergeToPdfAsync(template, mergeData, ct);
        var path = $"reports/{ctx.Form.Slug}/{ctx.Submission.Id.Replace(':', '_')}.pdf";
        await store.StoreAsync(path, pdf, "application/pdf", ct);          // Blob zuerst, Tabelle zuletzt (§14.2)
        ctx.Artifacts["report"] = path;
        return StepResult.Ok;
    }
}
