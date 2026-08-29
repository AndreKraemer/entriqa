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
/// </para>
/// <para>
/// The rule lives in the domain because all three layers need the same one: the steps resolve with it,
/// the publish check reports coverage with it, and the admin's step editor round-trips through it.
/// </para>
/// </summary>
public static class LValue
{
    /// <summary>The value for that language; fallback: the first entry present, exactly as <see cref="LText.Resolve"/>.</summary>
    public static JsonElement Resolve(JsonElement value, string? lang) => throw new NotImplementedException();

    /// <summary>Does the value have a version for this language (a plain scalar covers all)?</summary>
    public static bool Covers(JsonElement value, string lang) => throw new NotImplementedException();

    /// <summary>The declared languages the value has no version for - what the publish check reports.</summary>
    public static IReadOnlyList<string> MissingLocales(JsonElement value, IReadOnlyList<string> locales) => throw new NotImplementedException();

    /// <summary>Editor round trip, reading half: the stored value split into one entry per language.</summary>
    public static Dictionary<string, JsonNode?> Decompose(JsonNode? value, IReadOnlyList<string> locales) => throw new NotImplementedException();

    /// <summary>
    /// Editor round trip, writing half: collapses to a plain scalar when every declared language carries
    /// the same value, otherwise writes the locale object - the rule <c>LTextModel.ToNode</c> already
    /// applies to visitor texts.
    /// </summary>
    public static JsonNode? Compose(IReadOnlyDictionary<string, JsonNode?> perLocale, IReadOnlyList<string> locales) => throw new NotImplementedException();
}
