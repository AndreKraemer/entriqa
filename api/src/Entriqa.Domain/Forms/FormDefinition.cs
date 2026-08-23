using System.Text.Json;

namespace Entriqa.Domain.Forms;

/// <summary>
/// Die vollständige Definition eines Formulars, so wie sie als JSON in Table Storage liegt
/// (Entwurf im Forms-Eintrag, veröffentlicht als unveränderlicher Snapshot in FormVersions).
/// Besucherseitige Texte sind <see cref="LText"/> (String oder {locale: text}); vor Auslieferung
/// und Verarbeitung wird die Definition über <see cref="Localize"/> auf eine Sprache aufgelöst.
/// Der öffentliche Endpunkt liefert davon nur eine bereinigte Sicht (<see cref="PublicFormView"/>).
/// </summary>
public sealed record FormDefinition(
    string Slug,
    string Name,
    string Type,                                   // contact | leadmagnet | quiz – nur Voreinstellung, keine Logik
    LText? Intro,
    LText? SubmitLabel,
    IReadOnlyList<FieldDefinition> Fields,
    IReadOnlyList<StepDefinition> Pipeline,
    QuizDefinition? Quiz,
    CompletionDefinition Completion,
    bool Handling,                                 // Einsendungen bekommen Offen/Erledigt
    IReadOnlyList<string>? Locales = null)         // unterstützte Sprachen; null/leer = ["de"]
{
    public FieldDefinition? EmailField => Fields.FirstOrDefault(f => f.Type == FieldTypes.Email);
    public FieldDefinition? ConsentField => Fields.FirstOrDefault(f => f.Type == FieldTypes.Consent);

    public IReadOnlyList<string> EffectiveLocales => Locales is { Count: > 0 } ? Locales : new[] { "de" };
    public string DefaultLocale => EffectiveLocales[0];

    /// <summary>Nächstpassende Sprache: exakt, sonst Standard (erste deklarierte).</summary>
    public string MatchLocale(string? lang) =>
        lang is not null && EffectiveLocales.Contains(lang) ? lang : DefaultLocale;

    /// <summary>Auf eine Sprache aufgelöste Kopie – alle Texte verhalten sich danach wie einfache Strings.</summary>
    public FormDefinition Localize(string? lang)
    {
        var l = MatchLocale(lang);
        return this with
        {
            Intro = Intro?.Localized(l),
            SubmitLabel = SubmitLabel?.Localized(l),
            Fields = Fields.Select(f => f with
            {
                Label = f.Label.Localized(l),
                Placeholder = f.Placeholder?.Localized(l),
                Help = f.Help?.Localized(l),
                Text = f.Text?.Localized(l),
                Options = f.Options?.Select(o => o.Localized(l)).ToList(),
            }).ToList(),
            Quiz = Quiz?.Localize(l),
            Completion = Completion with { Message = Completion.Message?.Localized(l), Url = Completion.Url?.Localized(l) },
        };
    }
}

public sealed record FieldDefinition(
    string Id,
    string Type,
    LText Label,
    bool Required = false,
    LText? Placeholder = null,
    LText? Help = null,
    IReadOnlyList<LText>? Options = null,           // select, multiselect
    LText? Text = null,                             // consent
    string? Source = null,                          // hidden: utm_source | utm_medium | utm_campaign | referrer | page | query:<name> | fixed:<wert>
    int? MaxLength = null,
    decimal? Min = null,
    decimal? Max = null,                            // number: Obergrenze · rating: Stufen (Standard 5)
    bool BusinessOnly = false,                      // email: Freemail-Adressen ablehnen (nur Business-Adressen ins CRM)
    VisibleIfDefinition? VisibleIf = null);         // Feld nur zeigen/werten, wenn die Bedingung erfüllt ist

/// <summary>
/// Sichtbarkeitsbedingung: bezieht sich auf ein FRÜHERES Feld. Bei Auswahlfeldern zählen Options-Indizes
/// (sprachneutral – die Optionstexte sind je Sprache verschieden), sonst gilt "hat einen Wert" bzw. "ist angehakt".
/// </summary>
public sealed record VisibleIfDefinition(string Field, IReadOnlyList<int>? Options = null);

public static class FieldTypes
{
    public const string Text = "text", Email = "email", Number = "number", Textarea = "textarea",
        Select = "select", Multiselect = "multiselect", Checkbox = "checkbox", Date = "date",
        Consent = "consent", Hidden = "hidden", Section = "section", Divider = "divider",
        Tel = "tel", Rating = "rating", Page = "page", File = "file";

    public static readonly IReadOnlySet<string> All = new HashSet<string>
        { Text, Email, Number, Textarea, Select, Multiselect, Checkbox, Date, Consent, Hidden, Section, Divider, Tel, Rating, Page, File };

    /// <summary>Feldtypen, die keinen Wert tragen.</summary>
    public static bool IsLayout(string type) => type is Section or Divider or Page;
}

/// <summary>
/// Ein Schritt der Nachverarbeitung. <c>When</c>: always | hasEmail | result:{resultId}.
/// <c>Critical</c>: null = Standard des Schritts (Benachrichtigungen unkritisch, Rest kritisch);
/// unkritische Fehlschläge blockieren die Folgeschritte nicht.
/// </summary>
public sealed record StepDefinition(string Id, string Step, string When, JsonElement Config, bool? Critical = null);

public static class StepConditions
{
    public const string Always = "always", HasEmail = "hasEmail", ResultPrefix = "result:";
}

public sealed record CompletionDefinition(string Mode, LText? Message = null, LText? Url = null)
{
    public const string ModeMessage = "message", ModeRedirect = "redirect", ModeResult = "result";
}
