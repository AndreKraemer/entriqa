using System.Text.Json;
using System.Text.Json.Serialization;

namespace Entriqa.Domain.Forms;

/// <summary>
/// A visitor-facing text: either a plain string (applies to every language) or an object
/// per locale ({"de": "…", "en": "…"}). Both forms are valid in the definition JSON; delivery
/// (PublicFormView) and processing (validator, steps, QuizEngine) always work on the single-language
/// form resolved via <see cref="FormDefinition.Localize"/>.
/// </summary>
[JsonConverter(typeof(LTextConverter))]
public sealed class LText
{
    private readonly string? _single;
    private readonly IReadOnlyDictionary<string, string>? _map;

    public LText(string single) => _single = single;
    public LText(IReadOnlyDictionary<string, string> map) => _map = map;

    public bool IsEmpty => _single is null && (_map is null || _map.Count == 0);

    /// <summary>Text for that language; fallback: the first entry present.</summary>
    public string Resolve(string? lang) =>
        _single
        ?? (lang is not null && _map is not null && _map.TryGetValue(lang, out var v) ? v : null)
        ?? _map?.Values.FirstOrDefault()
        ?? string.Empty;

    /// <summary>Does the text have a version for this language (a plain string covers all)?</summary>
    public bool Covers(string lang) => _single is not null || (_map?.ContainsKey(lang) ?? false);

    /// <summary>Copy reduced to one language - afterwards the text behaves like a plain string.</summary>
    public LText Localized(string? lang) => _single is not null ? this : new LText(Resolve(lang));

    public static implicit operator LText(string s) => new(s);
    public override string ToString() => Resolve(null);

    internal string? Single => _single;
    internal IReadOnlyDictionary<string, string>? Map => _map;
}

public sealed class LTextConverter : JsonConverter<LText>
{
    public override LText Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String) return new LText(reader.GetString()!);
        if (reader.TokenType == JsonTokenType.StartObject)
            return new LText(JsonSerializer.Deserialize<Dictionary<string, string>>(ref reader, options)!);
        throw new JsonException("Text muss ein String oder ein Objekt {locale: text} sein.");
    }

    public override void Write(Utf8JsonWriter writer, LText value, JsonSerializerOptions options)
    {
        if (value.Single is not null) writer.WriteStringValue(value.Single);
        else JsonSerializer.Serialize(writer, value.Map ?? new Dictionary<string, string>(), options);
    }
}
