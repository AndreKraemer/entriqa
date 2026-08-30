using System.Text.Json;
using System.Text.Json.Nodes;

namespace Entriqa.Domain.Forms;

/// <summary>
/// A localizable step-configuration value: either a plain JSON scalar or array (applies to every
/// language) or an object per locale ({"de": 3, "en": 9}) - the same two shapes <see cref="LText"/>
/// allows for visitor texts, which is why neither form needs a migration.
/// <para>
/// At a position marked localizable an object is therefore always a locale map. That holds because no
/// localizable configuration field has an object value of its own (int, int[] and string only); on
/// <c>reportingcloud.pdf</c> the marker sits on the members of <c>templates</c>, not on the map itself.
/// <c>StepLocalizationTests</c> guards the invariant against the schemas.
/// </para>
/// <para>
/// The rule lives in the domain because all three layers need the same one: the steps resolve with it,
/// the publish check reports coverage with it, and the admin's step editor round-trips through it.
/// </para>
/// </summary>
public static class LValue
{
    /// <summary>
    /// Nothing configured. An empty string or an empty list counts as nothing, not as a value: the
    /// publish check would otherwise accept {"de": "", "en": "x"} as covering both languages and the
    /// step would ask the storage for a download link to "". The builder drops blanks on save, but the
    /// JSON tab and the admin API write a definition directly.
    /// </summary>
    private static bool IsBlank(JsonElement value) =>
        value.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null
        || (value.ValueKind == JsonValueKind.String && string.IsNullOrWhiteSpace(value.GetString()))
        || (value.ValueKind == JsonValueKind.Array && value.GetArrayLength() == 0);

    /// <summary>Nothing configured - as opposed to a value that merely lacks one language.</summary>
    private static bool IsEmpty(JsonElement value) =>
        IsBlank(value)
        || (value.ValueKind == JsonValueKind.Object && value.EnumerateObject().All(p => IsBlank(p.Value)));

    /// <summary>The value for that language; fallback: the first entry present, exactly as <see cref="LText.Resolve"/>.</summary>
    public static JsonElement Resolve(JsonElement value, string? lang)
    {
        if (value.ValueKind != JsonValueKind.Object) return value;         // a plain value applies to every language
        if (lang is not null && value.TryGetProperty(lang, out var mine) && !IsBlank(mine)) return mine;
        // Falling back beats failing: a step that sends the German template still delivers, while one that
        // throws blocks the pipeline. PublishCheckService is what stops this happening on purpose.
        foreach (var first in value.EnumerateObject())
            if (!IsBlank(first.Value)) return first.Value;
        return default;
    }

    /// <summary>Does the value have a version for this language (a plain value covers all)?</summary>
    public static bool Covers(JsonElement value, string lang)
    {
        if (IsEmpty(value)) return false;
        if (value.ValueKind != JsonValueKind.Object) return true;
        return value.TryGetProperty(lang, out var v) && !IsBlank(v);
    }

    /// <summary>
    /// The declared languages the value has no version for - what the publish check reports. A field with
    /// nothing configured at all reports none of them: that is the step's own "nothing chosen" check to
    /// make, and reporting it twice would only bury the useful message.
    /// </summary>
    public static IReadOnlyList<string> MissingLocales(JsonElement value, IReadOnlyList<string> locales) =>
        IsEmpty(value) ? Array.Empty<string>() : locales.Where(l => !Covers(value, l)).ToList();

    /// <summary>
    /// Editor round trip, reading half: the stored value split into one entry per language. Languages the
    /// form no longer declares are kept, so switching a language off and on again does not lose its value.
    /// </summary>
    public static Dictionary<string, JsonNode?> Decompose(JsonNode? value, IReadOnlyList<string> locales)
    {
        var perLocale = new Dictionary<string, JsonNode?>(StringComparer.Ordinal);
        foreach (var l in locales) perLocale[l] = null;

        switch (value)
        {
            case null:
                break;
            case JsonObject map:
                foreach (var (locale, node) in map) perLocale[locale] = node?.DeepClone();
                break;
            default:
                foreach (var l in perLocale.Keys.ToList()) perLocale[l] = value.DeepClone();   // covers every language
                break;
        }
        return perLocale;
    }

    /// <summary>
    /// Editor round trip, writing half: collapses to a plain value when every declared language carries the
    /// same one, otherwise writes the locale object - the rule <c>LTextModel.ToNode</c> already applies to
    /// visitor texts. Without the collapse, opening and saving an untranslated form would rewrite every
    /// scalar into {"de": x, "en": x}.
    /// </summary>
    public static JsonNode? Compose(IReadOnlyDictionary<string, JsonNode?> perLocale, IReadOnlyList<string> locales)
    {
        var filled = perLocale.Where(kv => kv.Value is not null).ToList();
        if (filled.Count == 0) return null;
        if (locales.All(l => filled.Any(kv => kv.Key == l))
            && filled.Select(kv => kv.Value!.ToJsonString()).Distinct(StringComparer.Ordinal).Count() == 1)
            return filled[0].Value!.DeepClone();
        return new JsonObject(filled.Select(kv => KeyValuePair.Create<string, JsonNode?>(kv.Key, kv.Value!.DeepClone())));
    }
}
