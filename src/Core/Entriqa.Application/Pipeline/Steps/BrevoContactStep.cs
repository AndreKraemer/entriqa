using System.Globalization;
using System.Text.Json;
using Entriqa.Application.Ports;
using Entriqa.Domain.Forms;

namespace Entriqa.Application.Pipeline.Steps;

/// <summary>Kontakt in Brevo anlegen/aktualisieren. Hinter doi.request platziert, setzt er DOUBLE_OPT-IN = Ja samt Datum.</summary>
public sealed class BrevoContactStep(IUpsertBrevoContactPort contacts, TimeProvider time) : ISubmissionStep
{
    public string Key => "brevo.contact";
    public string Name => "Kontakt in Brevo anlegen";
    public string Description => "Legt den Kontakt an oder aktualisiert ihn und trägt ihn in die gewählten Listen ein.";
    public StepMode Mode => StepMode.Inline;
    public IReadOnlyList<StepNeed> Needs => new[] { StepNeed.EmailField, StepNeed.ConsentField };
    public string ConfigSchema => """{"type":"object","required":["listIds"],"properties":{"listIds":{"type":"array","items":{"type":"integer"},"title":"Brevo-Listen","format":"brevo-list","localizable":true}}}""";

    public IEnumerable<string> CheckConfig(JsonElement config, FormDefinition form, IReadOnlySet<string> producedBefore)
    {
        if (config.GetIntList("listIds", form.DefaultLocale).Count == 0) yield return "keine Brevo-Liste gewählt.";
    }

    public async Task<StepResult> ExecuteAsync(StepContext ctx, JsonElement config, CancellationToken ct)
    {
        var attributes = new Dictionary<string, object?>();
        if (ctx.FirstName is not null) attributes["VORNAME"] = ctx.FirstName;
        if (LastNameOf(ctx.Form, ctx.Submission.Values) is { } lastName) attributes["NACHNAME"] = lastName;
        if (ctx.Submission.Locale is { Length: > 0 } locale) attributes["SPRACHE"] = locale;   // segmentation: campaigns in the right language
        if (ctx.Submission.IsConfirmed)
        {
            attributes["DOUBLE_OPT-IN"] = 1;                                   // Brevo-Standardattribut (Kategorie: 1 = Ja)
            attributes["OPT_IN_DATE"] = ctx.Submission.ConfirmedAt!.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }
        attributes["LAST_FORM"] = ctx.Form.Slug;
        attributes["LAST_FORM_AT"] = time.GetUtcNow().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        ctx.Submission.BrevoContactId = await contacts.UpsertAsync(ctx.Email!, config.GetIntList("listIds", ctx.Submission.Locale), attributes, ct);
        return StepResult.Ok;
    }

    /// <summary>Last name by label heuristic - the counterpart to the first-name detection on submit.</summary>
    private static string? LastNameOf(FormDefinition def, IReadOnlyDictionary<string, string> values)
    {
        static string L(FieldDefinition f) => f.Label.ToString();
        var f = def.Fields.FirstOrDefault(x => x.Type == FieldTypes.Text
                    && (L(x).Contains("Nachname", StringComparison.OrdinalIgnoreCase) || L(x).Contains("last name", StringComparison.OrdinalIgnoreCase)));
        if (f is not null) return values.TryGetValue(f.Id, out var v) && v.Length > 0 ? v : null;

        // Only one "name" field: everything after the first space (the same split yields the first name).
        var name = def.Fields.FirstOrDefault(x => x.Type == FieldTypes.Text && L(x).Equals("Name", StringComparison.OrdinalIgnoreCase));
        if (name is null || !values.TryGetValue(name.Id, out var full)) return null;
        var parts = full.Split(' ', 2, StringSplitOptions.TrimEntries);
        return parts.Length == 2 && parts[1].Length > 0 ? parts[1] : null;
    }
}
