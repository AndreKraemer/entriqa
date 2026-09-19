using System.Text.Json;
using Entriqa.Application.Ports;
using Entriqa.Application.Security;
using Entriqa.Domain.Forms;
using Entriqa.Domain.UseCases;

namespace Entriqa.Application.Pipeline.Steps;

/// <summary>
/// Double opt-in, built in house: sends the confirmation mail through Brevo, the link points at the static
/// confirmation page (/bestaetigen/?t=…) whose button confirms via POST /api/confirm.
/// Every step after it runs only once confirmed (SplitsPhase). The evidence (timestamp, IP hash,
/// consent text inside the form version) stays with us.
/// </summary>
public sealed class DoiRequestStep(ISendTransactionalMailPort mail, FormTokenService tokens) : ISubmissionStep
{
    public string Key => "doi.request";
    public string Name => "Bestätigung anfordern (Double-Opt-In)";
    public string Description => "Verschickt eine neutrale Bestätigungsmail mit Link. Alles, was danach kommt, läuft erst nach dem Klick.";
    public StepMode Mode => StepMode.Inline;
    public bool SplitsPhase => true;
    public StepTestBehavior TestBehavior => StepTestBehavior.Redirected;    // #21: the confirmation mail goes to the admin
    public IReadOnlyList<StepNeed> Needs => new[] { StepNeed.EmailField, StepNeed.ConsentField };
    public string ConfigSchema => """{"type":"object","required":["templateId"],"properties":{"templateId":{"type":"integer","title":"Bestätigungsmail (Brevo-Vorlage)","format":"brevo-template","localizable":true}}}""";
    public IReadOnlyList<MailParam> MailParams => new MailParam[]
    {
        new("confirmUrl", "Bestätigungslink – als Ziel des Buttons eintragen (Pflicht)"),
        new("firstName", "Vorname aus dem Formular (kann leer sein)"),
        new("form", "Name des Formulars"),
        new("site", "Name der Website"),
        new("validDays", "Gültigkeit des Links in Tagen"),
    };

    public IEnumerable<string> CheckConfig(JsonElement config, FormDefinition form, IReadOnlySet<string> producedBefore)
    {
        if (config.GetInt("templateId", form.DefaultLocale) is null) yield return "keine Brevo-Vorlage für die Bestätigungsmail.";
    }

    public async Task<StepResult> ExecuteAsync(StepContext ctx, JsonElement config, CancellationToken ct)
    {
        if (ctx.Submission.IsConfirmed) return StepResult.Ok;              // another run after confirmation: nothing to do
        // In a test the link carries a test-confirm token: clicking it confirms nothing and shows a test hint (#21, AC9).
        var kind = ctx.Test is null ? FormTokenService.KindConfirm : FormTokenService.KindConfirmTest;
        var token = tokens.Issue(kind, ctx.Submission.Id);
        // The link points at the static confirmation page; only its button posts to /api/confirm.
        // Never straight at a GET endpoint - link scanners (Outlook SafeLinks and friends) would confirm the opt-in.
        var confirmUrl = $"{ctx.Options.BaseUrl.TrimEnd('/')}{ctx.Options.ConfirmPagePathFor(ctx.Submission.Locale)}?t={Uri.EscapeDataString(token)}";

        var parameters = new Dictionary<string, object?>
        {
            ["confirmUrl"] = confirmUrl,
            ["firstName"] = ctx.FirstName,
            ["form"] = ctx.Form.Name,
            ["site"] = ctx.Options.SiteName,
            ["validDays"] = ctx.Options.ConfirmTokenDays,
        };
        var to = ctx.Test?.MailTo ?? ctx.Email!;                            // #21: in a test the mail goes to the admin
        await mail.SendAsync(to, ctx.FirstName, config.GetInt("templateId", ctx.Submission.Locale)!.Value, parameters, null, ct);
        return StepResult.Ok;
    }
}
