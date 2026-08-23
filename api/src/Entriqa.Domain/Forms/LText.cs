using System.Text.Json;
using System.Text.Json.Serialization;

namespace Entriqa.Domain.Forms;

/// <summary>
/// Ein besucherseitiger Text: entweder ein einfacher String (gilt für alle Sprachen) oder ein Objekt
/// je Locale ({"de": "…", "en": "…"}). Im JSON der Definition sind beide Formen gültig; die Auslieferung
/// (PublicFormView) und die Verarbeitung (Validator, Steps, QuizEngine) arbeiten immer auf der über
/// <see cref="FormDefinition.Localize"/> aufgelösten, einsprachigen Fassung.
/// </summary>
[JsonConverter(typeof(LTextConverter))]
public sealed class LText
{
    private readonly string? _single;
    private readonly IReadOnlyDictionary<string, string>? _map;

    public LText(string single) => _single = single;
    public LText(IReadOnlyDictionary<string, string> map) => _map = map;

    public bool IsEmpty => _single is null && (_map is null || _map.Count == 0);

    /// <summary>Text für die Sprache; Fallback: erster vorhandener Eintrag.</summary>
    public string Resolve(string? lang) =>
        _single
        ?? (lang is not null && _map is not null && _map.TryGetValue(lang, out var v) ? v : null)
        ?? _map?.Values.FirstOrDefault()
        ?? string.Empty;

    /// <summary>Hat der Text eine Fassung für diese Sprache (einfacher String gilt für alle)?</summary>
    public bool Covers(string lang) => _single is not null || (_map?.ContainsKey(lang) ?? false);

    /// <summary>Auf eine Sprache eingedampfte Kopie – danach verhält sich der Text wie ein einfacher String.</summary>
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
