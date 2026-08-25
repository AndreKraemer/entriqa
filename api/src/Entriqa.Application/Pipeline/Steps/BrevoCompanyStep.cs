using System.Text.Json;
using Entriqa.Application.Ports;
using Entriqa.Domain.Forms;
using Entriqa.Domain.Submissions;

namespace Entriqa.Application.Pipeline.Steps;

/// <summary>
/// Company upkeep in the Brevo CRM: find or create the company and link the contact.
/// Companies are the primary place for company data (rather than just the FIRMA contact attribute);
/// belongs AFTER brevo.contact so that the contact exists.
/// </summary>
public sealed class BrevoCompanyStep(IUpsertBrevoCompanyPort companies) : ISubmissionStep
{
    public string Key => "brevo.company";
    public string Name => "Firma in Brevo pflegen";
    public string Description => "Sucht die Firma im Brevo-CRM nach Namen, legt sie bei Bedarf an und verknüpft den Kontakt mit ihr.";
    public StepMode Mode => StepMode.Inline;
    public IReadOnlyList<StepNeed> Needs => new[] { StepNeed.EmailField };
    public string ConfigSchema => """{"type":"object","required":["field"],"properties":{"field":{"type":"string","title":"Feld mit dem Firmennamen","format":"form-field"}}}""";

    public IEnumerable<string> CheckConfig(JsonElement config, FormDefinition form, IReadOnlySet<string> producedBefore)
    {
        var fieldId = config.GetString("field");
        if (string.IsNullOrEmpty(fieldId)) { yield return "kein Feld mit dem Firmennamen gewählt."; yield break; }
        var field = form.Fields.FirstOrDefault(f => f.Id == fieldId);
        if (field is null) yield return $"Feld '{fieldId}' gibt es nicht.";
        else if (FieldTypes.IsLayout(field.Type) || field.Type is FieldTypes.Checkbox or FieldTypes.Consent)
            yield return $"Feld '{fieldId}' kann keinen Firmennamen tragen.";
        if (!form.Pipeline.Any(s => s.Step == "brevo.contact"))
            yield return "braucht einen \"Kontakt in Brevo anlegen\"-Schritt davor, damit der Kontakt existiert.";
    }

    public async Task<StepResult> ExecuteAsync(StepContext ctx, JsonElement config, CancellationToken ct)
    {
        var fieldId = config.GetString("field") ?? "";
        ctx.Submission.Values.TryGetValue(fieldId, out var name);
        if (string.IsNullOrWhiteSpace(name) || ctx.Email is null)
            return new StepResult(StepRunStatus.Skipped);                 // no company given - not an error

        await companies.UpsertCompanyAsync(name.Trim(), ctx.Email, ct);
        return StepResult.Ok;
    }
}
