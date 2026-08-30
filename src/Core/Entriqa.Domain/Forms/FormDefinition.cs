using System.Text.Json;
using Entriqa.Domain.Validation;

namespace Entriqa.Domain.Forms;

/// <summary>
/// The complete definition of a form, exactly as it is stored as JSON in table storage
/// (draft in the Forms entry, published as an immutable snapshot in FormVersions).
/// Visitor-facing texts are <see cref="LText"/> (string or {locale: text}); before delivery
/// and processing the definition is resolved to a single language via <see cref="Localize"/>.
/// The public endpoint only serves a sanitized view of it (<see cref="PublicFormView"/>).
/// </summary>
public sealed record FormDefinition(
    string Slug,
    string Name,
    string Type,                                   // contact | leadmagnet | quiz - a default only, no logic attached
    LText? Intro,
    LText? SubmitLabel,
    IReadOnlyList<FieldDefinition> Fields,
    IReadOnlyList<StepDefinition> Pipeline,
    QuizDefinition? Quiz,
    CompletionDefinition Completion,
    bool Handling,                                 // submissions get an open/done state
    IReadOnlyList<string>? Locales = null)         // supported languages; null/empty = ["de"]
{
    // The admin always writes both collections, hand-written or imported JSON does not have to.
    // A missing key used to arrive as null and take the publish check down with a
    // NullReferenceException - an unhandled error instead of a reportable finding.
    public IReadOnlyList<FieldDefinition> Fields { get; init; } = Fields ?? Array.Empty<FieldDefinition>();
    public IReadOnlyList<StepDefinition> Pipeline { get; init; } = Pipeline ?? Array.Empty<StepDefinition>();

    public FieldDefinition? EmailField => Fields.FirstOrDefault(f => f.Type == FieldTypes.Email);
    public FieldDefinition? ConsentField => Fields.FirstOrDefault(f => f.Type == FieldTypes.Consent);

    /// <summary>
    /// Whether the visitor actually ticked the consent (#1). Having a consent field is not consent:
    /// it may be defined as optional, and Submission.ConsentText is filled either way.
    /// </summary>
    public bool ConsentGiven(IReadOnlyDictionary<string, string> values) =>
        ConsentField is { } f && values.TryGetValue(f.Id, out var v) && FormSubmissionValidator.IsTrue(v);

    public IReadOnlyList<string> EffectiveLocales => Locales is { Count: > 0 } ? Locales : new[] { "de" };
    public string DefaultLocale => EffectiveLocales[0];

    /// <summary>Closest matching language: exact, otherwise the default (the first one declared).</summary>
    public string MatchLocale(string? lang) =>
        lang is not null && EffectiveLocales.Contains(lang) ? lang : DefaultLocale;

    /// <summary>Copy resolved to one language - every text behaves like a plain string afterwards.</summary>
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
    string? Source = null,                          // hidden: utm_source | utm_medium | utm_campaign | referrer | page | query:<name> | fixed:<value>
    int? MaxLength = null,
    decimal? Min = null,
    decimal? Max = null,                            // number: upper bound · rating: number of steps (default 5)
    bool BusinessOnly = false,                      // email: reject freemail addresses (only business addresses reach the CRM)
    VisibleIfDefinition? VisibleIf = null);         // only show and evaluate the field while the condition holds

/// <summary>
/// Visibility condition: refers to an EARLIER field. For choice fields the option indexes count
/// (language-neutral - the option texts differ per language), otherwise "has a value" resp. "is ticked" applies.
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

    /// <summary>Field types that carry no value.</summary>
    public static bool IsLayout(string type) => type is Section or Divider or Page;
}

/// <summary>
/// One post-processing step. <c>When</c>: always | hasEmail | result:{resultId}.
/// <c>Critical</c>: null = the step's own default (notifications non-critical, everything else critical);
/// non-critical failures do not block the following steps.
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
