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
    public string ConfigSchema => """{"type":"object","required":["listIds"],"properties":{"listIds":{"type":"array","items":{"type":"integer"},"title":"Brevo-Listen","format":"brevo-list"},"attribute":{"type":"string","title":"Quiz-Ergebnis in Kontakt-Attribut schreiben (optional)"},"sourceAttribute":{"type":"string","title":"Quelle (utm_source) in Attribut schreiben (optional)"}}}""";

    public IEnumerable<string> CheckConfig(JsonElement config, FormDefinition form, IReadOnlySet<string> producedBefore)
    {
        if (config.GetIntList("listIds").Count == 0) yield return "keine Brevo-Liste gewählt.";
    }

    public async Task<StepResult> ExecuteAsync(StepContext ctx, JsonElement config, CancellationToken ct)
    {
        var attributes = new Dictionary<string, object?>();
        if (ctx.FirstName is not null) attributes["VORNAME"] = ctx.FirstName;
        if (LastNameOf(ctx.Form, ctx.Submission.Values) is { } lastName) attributes["NACHNAME"] = lastName;
        if (ctx.Submission.Locale is { Length: > 0 } locale) attributes["SPRACHE"] = locale;   // Segmentierung: Kampagnen in der richtigen Sprache
        if (ctx.Submission.IsConfirmed)
        {
            attributes["DOUBLE_OPT-IN"] = 1;                                   // Brevo-Standardattribut (Kategorie: 1 = Ja)
            attributes["OPT_IN_DATE"] = ctx.Submission.ConfirmedAt!.Value.ToString("yyyy-MM-dd");
        }
        if (config.GetString("attribute") is { Length: > 0 } attr && ctx.QuizResult is not null) attributes[attr] = ctx.QuizResult.Title;
        if (config.GetString("sourceAttribute") is { Length: > 0 } src && ctx.Submission.Source is not null) attributes[src] = ctx.Submission.Source;
        attributes["LAST_FORM"] = ctx.Form.Slug;
        attributes["LAST_FORM_AT"] = time.GetUtcNow().ToString("yyyy-MM-dd");

        ctx.Submission.BrevoContactId = await contacts.UpsertAsync(ctx.Email!, config.GetIntList("listIds"), attributes, ct);
        return StepResult.Ok;
    }

    /// <summary>Nachname per Label-Heuristik – Gegenstück zur Vornamen-Erkennung beim Absenden.</summary>
    private static string? LastNameOf(FormDefinition def, IReadOnlyDictionary<string, string> values)
    {
        static string L(FieldDefinition f) => f.Label.ToString();
        var f = def.Fields.FirstOrDefault(x => x.Type == FieldTypes.Text
                    && (L(x).Contains("Nachname", StringComparison.OrdinalIgnoreCase) || L(x).Contains("last name", StringComparison.OrdinalIgnoreCase)));
        if (f is not null) return values.TryGetValue(f.Id, out var v) && v.Length > 0 ? v : null;

        // Nur ein "Name"-Feld: alles nach dem ersten Leerzeichen (dieselbe Aufteilung liefert den Vornamen).
        var name = def.Fields.FirstOrDefault(x => x.Type == FieldTypes.Text && L(x).Equals("Name", StringComparison.OrdinalIgnoreCase));
        if (name is null || !values.TryGetValue(name.Id, out var full)) return null;
        var parts = full.Split(' ', 2, StringSplitOptions.TrimEntries);
        return parts.Length == 2 && parts[1].Length > 0 ? parts[1] : null;
    }
}
