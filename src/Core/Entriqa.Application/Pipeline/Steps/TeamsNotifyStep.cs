using System.Text.Json;
using Entriqa.Application.Ports;
using Entriqa.Domain.Forms;

namespace Entriqa.Application.Pipeline.Steps;

/// <summary>
/// Teams message per submission: an adaptive card to a Power Automate Workflows webhook of the channel
/// (Teams: "Workflows" -> "Post to a channel when a webhook request is received"). The classic
/// Office 365 incoming webhooks are retired and deliberately not supported.
/// </summary>
public sealed class TeamsNotifyStep(IPostWebhookPort webhook) : ISubmissionStep
{
    public string Key => "teams.notify";
    public string Name => "Teams-Nachricht";
    public string Description => "Postet die Einsendung als Karte in einen Teams-Kanal (Workflows-Webhook-URL des Kanals).";
    public StepMode Mode => StepMode.Inline;
    public bool CriticalByDefault => false;
    public string ConfigSchema => """{"type":"object","required":["webhookUrl"],"properties":{"webhookUrl":{"type":"string","format":"uri","title":"Workflows-Webhook-URL des Kanals"},"title":{"type":"string","title":"Kartentitel (Platzhalter wie beim Webhook)","default":"Neue Einsendung: {{form}}"}}}""";

    public IEnumerable<string> CheckConfig(JsonElement config, FormDefinition form, IReadOnlySet<string> producedBefore)
    {
        if (!Uri.TryCreate(config.GetString("webhookUrl"), UriKind.Absolute, out var u) || u.Scheme != "https") yield return "Webhook-URL fehlt oder ist kein https.";
    }

    public IReadOnlyList<Entriqa.Domain.UseCases.TestNote> DescribeTest(StepContext ctx, JsonElement config)   // #21
    {
        return new[] { new Entriqa.Domain.UseCases.TestNote(Entriqa.Domain.Validation.ValidationMessages.TestNoteTeams, config.GetString("webhookUrl") ?? "") };
    }

    public async Task<StepResult> ExecuteAsync(StepContext ctx, JsonElement config, CancellationToken ct)
    {
        var title = PayloadTemplate.Fill(config.GetString("title") ?? "Neue Einsendung: {{form}}", ctx);
        var facts = ctx.LabeledValues()
            .Select(kv => new { title = kv.Key, value = Truncate(kv.Value?.ToString() ?? "", 300) })
            .ToList<object>();
        if (ctx.Submission.Quiz is { } q)
            facts.Add(new { title = "Quiz-Ergebnis", value = $"{ctx.QuizResult?.Title.ToString() ?? q.ResultId} ({q.Pct} %)" });

        var card = new
        {
            type = "message",
            attachments = new object[]
            {
                new
                {
                    contentType = "application/vnd.microsoft.card.adaptive",
                    content = new
                    {
                        type = "AdaptiveCard",
                        version = "1.4",
                        schema = "http://adaptivecards.io/schemas/adaptive-card.json",
                        body = new object[]
                        {
                            new { type = "TextBlock", size = "Medium", weight = "Bolder", text = title, wrap = true },
                            new { type = "TextBlock", isSubtle = true, spacing = "None", wrap = true,
                                  text = $"{ctx.Options.SiteName} · {ctx.Submission.CreatedAt:dd.MM.yyyy HH:mm}" },
                            new { type = "FactSet", facts },
                        },
                        actions = new object[]
                        {
                            new { type = "Action.OpenUrl", title = "Im Admin öffnen",
                                  url = $"{ctx.Options.BaseUrl.TrimEnd('/')}/admin/submissions/{Uri.EscapeDataString(ctx.Submission.Id)}" },
                        },
                    },
                },
            },
        };
        await webhook.PostJsonAsync(new Uri(config.GetString("webhookUrl")!), card, ct);
        return StepResult.Ok;
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "…";
}
