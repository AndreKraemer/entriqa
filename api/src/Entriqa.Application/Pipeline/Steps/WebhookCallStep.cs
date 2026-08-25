using System.Text.Json;
using Entriqa.Application.Ports;
using Entriqa.Domain.Forms;

namespace Entriqa.Application.Pipeline.Steps;

/// <summary>
/// POST to an https URL - the generic carrier for everything behind Power Automate (to-do task, Planner, …).
/// Without <c>payload</c> the whole submission goes out as standard JSON; with <c>payload</c> the JSON template
/// stored there is sent instead and placeholders in string values are replaced (see <see cref="PayloadTemplate"/>).
/// </summary>
public sealed class WebhookCallStep(IPostWebhookPort webhook) : ISubmissionStep
{
    public string Key => "webhook.call";
    public string Name => "Webhook aufrufen";
    public string Description => "Schickt die Einsendung als JSON an eine URL – z. B. an einen Power-Automate-Flow, der eine To-Do-Aufgabe anlegt.";
    public StepMode Mode => StepMode.Inline;
    public bool CriticalByDefault => false;
    public string ConfigSchema => """{"type":"object","required":["url"],"properties":{"url":{"type":"string","format":"uri","title":"URL"},"payload":{"type":"object","title":"Eigenes JSON (Platzhalter: {{form}}, {{slug}}, {{submissionId}}, {{receivedAt}}, {{locale}}, {{field:<id>}}, {{quiz.pct}}, {{quiz.resultId}}, {{quiz.resultTitle}}, {{adminUrl}})"}}}""";

    public IEnumerable<string> CheckConfig(JsonElement config, FormDefinition form, IReadOnlySet<string> producedBefore)
    {
        if (!Uri.TryCreate(config.GetString("url"), UriKind.Absolute, out var u) || u.Scheme != "https") yield return "URL fehlt oder ist kein https.";
    }

    public async Task<StepResult> ExecuteAsync(StepContext ctx, JsonElement config, CancellationToken ct)
    {
        object payload = config.ValueKind == JsonValueKind.Object && config.TryGetProperty("payload", out var tpl) && tpl.ValueKind == JsonValueKind.Object
            ? PayloadTemplate.Render(tpl, ctx) ?? new { }
            : new
            {
                form = ctx.Form.Slug,
                name = ctx.Form.Name,
                submissionId = ctx.Submission.Id,
                receivedAt = ctx.Submission.CreatedAt,
                locale = ctx.Submission.Locale,
                values = ctx.LabeledValues(),
                quiz = ctx.Submission.Quiz is null ? null : new { ctx.Submission.Quiz.ResultId, Title = ctx.QuizResult?.Title.ToString(), ctx.Submission.Quiz.Pct },
            };
        await webhook.PostJsonAsync(new Uri(config.GetString("url")!), payload, ct);
        return StepResult.Ok;
    }
}

/// <summary>Ersetzt {{…}}-Platzhalter in allen String-Werten eines JSON-Templates. Unbekannte Platzhalter bleiben stehen.</summary>
public static class PayloadTemplate
{
    public static object? Render(JsonElement template, StepContext ctx) => Walk(template, ctx);

    private static object? Walk(JsonElement e, StepContext ctx) => e.ValueKind switch
    {
        JsonValueKind.Object => e.EnumerateObject().ToDictionary(p => p.Name, p => Walk(p.Value, ctx)),
        JsonValueKind.Array => e.EnumerateArray().Select(x => Walk(x, ctx)).ToList(),
        JsonValueKind.String => Fill(e.GetString()!, ctx),
        JsonValueKind.Number => e.GetDecimal(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        _ => null,
    };

    public static string Fill(string s, StepContext ctx) =>
        System.Text.RegularExpressions.Regex.Replace(s, @"\{\{([a-zA-Z0-9._:-]+)\}\}", m => ResolveToken(m.Groups[1].Value, ctx) ?? m.Value);

    private static string? ResolveToken(string token, StepContext ctx) => token switch
    {
        "form" => ctx.Form.Name,
        "slug" => ctx.Form.Slug,
        "submissionId" => ctx.Submission.Id,
        "receivedAt" => ctx.Submission.CreatedAt.ToString("O"),
        "locale" => ctx.Submission.Locale ?? "",
        "quiz.pct" => ctx.Submission.Quiz?.Pct.ToString(),
        "quiz.resultId" => ctx.Submission.Quiz?.ResultId,
        "quiz.resultTitle" => ctx.QuizResult?.Title.ToString(),
        "adminUrl" => $"{ctx.Options.BaseUrl.TrimEnd('/')}/admin/submissions/{Uri.EscapeDataString(ctx.Submission.Id)}",
        var t when t.StartsWith("field:", StringComparison.Ordinal) => ctx.Submission.Values.GetValueOrDefault(t[6..]) ?? "",
        _ => null,
    };
}
