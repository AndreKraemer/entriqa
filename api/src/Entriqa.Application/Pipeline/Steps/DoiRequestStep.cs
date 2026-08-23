using System.Text.Json;
using Entriqa.Application.Ports;
using Entriqa.Application.Security;
using Entriqa.Domain.Forms;
using Entriqa.Domain.UseCases;

namespace Entriqa.Application.Pipeline.Steps;

/// <summary>
/// Double-Opt-In, selbst gebaut: verschickt die Bestätigungsmail über Brevo, der Link zeigt auf die statische
/// Bestätigungsseite (/bestaetigen/?t=…), deren Button per POST /api/confirm bestätigt.
/// Alle Schritte danach laufen erst nach der Bestätigung (SplitsPhase). Der Nachweis (Zeitpunkt, IP-Hash,
/// Einwilligungstext in der Formularversion) liegt bei uns.
/// </summary>
public sealed class DoiRequestStep(ISendTransactionalMailPort mail, FormTokenService tokens) : ISubmissionStep
{
    public string Key => "doi.request";
    public string Name => "Bestätigung anfordern (Double-Opt-In)";
    public string Description => "Verschickt eine neutrale Bestätigungsmail mit Link. Alles, was danach kommt, läuft erst nach dem Klick.";
    public StepMode Mode => StepMode.Inline;
    public bool SplitsPhase => true;
    public IReadOnlyList<StepNeed> Needs => new[] { StepNeed.EmailField, StepNeed.ConsentField };
    public string ConfigSchema => """{"type":"object","required":["templateId"],"properties":{"templateId":{"type":"integer","title":"Bestätigungsmail (Brevo-Vorlage)","format":"brevo-template"}}}""";
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
        if (config.GetInt("templateId") is null) yield return "keine Brevo-Vorlage für die Bestätigungsmail.";
    }

    public async Task<StepResult> ExecuteAsync(StepContext ctx, JsonElement config, CancellationToken ct)
    {
        if (ctx.Submission.IsConfirmed) return StepResult.Ok;              // erneuter Lauf nach Bestätigung: nichts zu tun
        var token = tokens.Issue(FormTokenService.KindConfirm, ctx.Submission.Id);
        // Link zeigt auf die statische Bestätigungsseite; erst deren Button macht den POST auf /api/confirm.
        // Nie direkt auf einen GET-Endpunkt – Link-Scanner (Outlook SafeLinks & Co.) würden das Opt-in bestätigen.
        var confirmUrl = $"{ctx.Options.BaseUrl.TrimEnd('/')}{ctx.Options.ConfirmPagePath}?t={Uri.EscapeDataString(token)}";

        var parameters = new Dictionary<string, object?>
        {
            ["confirmUrl"] = confirmUrl,
            ["firstName"] = ctx.FirstName,
            ["form"] = ctx.Form.Name,
            ["site"] = ctx.Options.SiteName,
            ["validDays"] = ctx.Options.ConfirmTokenDays,
        };
        await mail.SendAsync(ctx.Email!, ctx.FirstName, config.GetInt("templateId")!.Value, parameters, null, ct);
        return StepResult.Ok;
    }
}
