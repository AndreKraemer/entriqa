using System.Globalization;
using System.Text.Json;
using Entriqa.Domain.Errors;
using Entriqa.Domain.Forms;

namespace Entriqa.Domain.Validation;

/// <summary>
/// The single source of the field rules: derives them at runtime from the published definition.
/// forms.js mirrors the same rules for UX only. Reports all errors at once.
/// Expects a definition already resolved via <see cref="FormDefinition.Localize"/>;
/// <paramref name="locale"/> only controls the language of the error messages.
/// </summary>
public static class FormSubmissionValidator
{
    public static void ValidateAndThrow(FormDefinition def, Dictionary<string, string> values, Dictionary<string, string>? answers,
        string? locale = null, IReadOnlySet<string>? extraFreemailDomains = null)
    {
        var m = ValidationMessages.For(locale);
        var errors = new List<FieldError>();

        foreach (var f in def.Fields)
        {
            if (FieldTypes.IsLayout(f.Type)) continue;

            // Conditionally invisible fields: skip validation and drop the value (stale input).
            // Conditions point at earlier fields (publish check), so a single pass in order is enough.
            if (!IsVisible(f, def, values))
            {
                values.Remove(f.Id);
                continue;
            }

            values.TryGetValue(f.Id, out var raw);
            var value = raw?.Trim() ?? string.Empty;

            if (f.Type == FieldTypes.Hidden)
            {
                if (value.Length > 200) errors.Add(new(f.Id, m[ValidationMessages.TooLong]));
                continue;
            }

            var required = f.Required || (f.Type == FieldTypes.Email && def.Quiz?.CollectEmail == "required");
            if (string.IsNullOrEmpty(value) || (f.Type is FieldTypes.Consent or FieldTypes.Checkbox && !IsTrue(value)))
            {
                if (required) errors.Add(new(f.Id, f.Type == FieldTypes.Consent ? m[ValidationMessages.ConsentRequired] : m[ValidationMessages.Required]));
                continue;
            }

            switch (f.Type)
            {
                case FieldTypes.Email:
                    if (!LooksLikeEmail(value)) errors.Add(new(f.Id, m[ValidationMessages.Email]));
                    else if (f.BusinessOnly && FreemailDomains.IsFreemail(value, extraFreemailDomains))
                        errors.Add(new(f.Id, m[ValidationMessages.BusinessEmail]));
                    break;
                case FieldTypes.Text:
                    if (value.Length > (f.MaxLength ?? 200)) errors.Add(new(f.Id, m[ValidationMessages.MaxLength].Replace("{max}", (f.MaxLength ?? 200).ToString())));
                    break;
                case FieldTypes.Textarea:
                    if (value.Length > (f.MaxLength ?? 4000)) errors.Add(new(f.Id, m[ValidationMessages.MaxLength].Replace("{max}", (f.MaxLength ?? 4000).ToString())));
                    break;
                case FieldTypes.Number:
                    if (!decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var n)
                        && !decimal.TryParse(value, NumberStyles.Number, CultureInfo.GetCultureInfo("de-DE"), out n))
                        errors.Add(new(f.Id, m[ValidationMessages.Number]));
                    else if ((f.Min is not null && n < f.Min) || (f.Max is not null && n > f.Max))
                        errors.Add(new(f.Id, m[ValidationMessages.NumberRange]
                            .Replace("{min}", f.Min?.ToString(CultureInfo.InvariantCulture) ?? "…")
                            .Replace("{max}", f.Max?.ToString(CultureInfo.InvariantCulture) ?? "…")));
                    break;
                case FieldTypes.Date:
                    if (!DateOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out _)) errors.Add(new(f.Id, m[ValidationMessages.Date]));
                    break;
                case FieldTypes.Select:
                    if (!OptionValues(f).Contains(value)) errors.Add(new(f.Id, m[ValidationMessages.Choice]));
                    break;
                case FieldTypes.Multiselect:
                    var allowed = OptionValues(f);
                    foreach (var part in SplitMulti(value))
                        if (!allowed.Contains(part)) { errors.Add(new(f.Id, m[ValidationMessages.Choice])); break; }
                    break;
                case FieldTypes.Tel:
                    if (!LooksLikePhone(value)) errors.Add(new(f.Id, m[ValidationMessages.Tel]));
                    break;
                case FieldTypes.Rating:
                    var steps = (int)(f.Max ?? 5);
                    if (!int.TryParse(value, out var rating) || rating < 1 || rating > steps)
                        errors.Add(new(f.Id, m[ValidationMessages.Choice]));
                    break;
                case FieldTypes.File:
                    var handle = UploadHandle.TryParse(value);
                    if (handle is null
                        || !(handle.Path.StartsWith("uploads/", StringComparison.Ordinal) || handle.Path.StartsWith("attachments/", StringComparison.Ordinal))
                        || handle.Path.Contains("..", StringComparison.Ordinal)
                        || !UploadRules.IsAllowed(handle.Name) || handle.Size > UploadRules.MaxBytes)
                        errors.Add(new(f.Id, m[ValidationMessages.UploadInvalid]));
                    break;
            }
        }

        // Consent may be defined as optional (a quiz with an optional address, say), but as soon as an address is given it has to be there.
        if (def.ConsentField is { Required: false } consent && def.EmailField is { } emailField
            && values.TryGetValue(emailField.Id, out var mail) && !string.IsNullOrWhiteSpace(mail)
            && !(values.TryGetValue(consent.Id, out var c) && IsTrue(c)))
            errors.Add(new(consent.Id, m[ValidationMessages.ConsentIfEmail]));

        if (def.Quiz is not null && (answers is null || answers.Count == 0))
            errors.Add(new("_quiz", m[ValidationMessages.QuizMissing]));

        if (errors.Count > 0) throw new ValidationException(errors);
    }

    /// <summary>Evaluates a visibility condition against the (already sanitized) values.</summary>
    public static bool IsVisible(FieldDefinition f, FormDefinition def, IReadOnlyDictionary<string, string> values)
    {
        if (f.VisibleIf is not { } cond) return true;
        var other = def.Fields.FirstOrDefault(x => x.Id == cond.Field);
        if (other is null) return true;                                     // a broken reference blocks nothing - the publish check reports it
        values.TryGetValue(other.Id, out var raw);
        var value = raw?.Trim() ?? string.Empty;

        if (other.Type is FieldTypes.Checkbox or FieldTypes.Consent) return IsTrue(value);
        if (cond.Options is { Count: > 0 } idx && other.Options is { Count: > 0 } opts)
        {
            var wanted = idx.Where(i => i >= 0 && i < opts.Count).Select(i => opts[i].ToString()).ToHashSet();
            if (other.Type == FieldTypes.Multiselect) return SplitMulti(value).Any(wanted.Contains);
            return wanted.Contains(value);
        }
        return value.Length > 0;
    }

    private static bool LooksLikePhone(string v) =>
        v.Length is >= 5 and <= 30 && v.Count(char.IsAsciiDigit) >= 5
        && v.All(c => char.IsAsciiDigit(c) || c is ' ' or '+' or '-' or '/' or '(' or ')' or '.');

    private static HashSet<string> OptionValues(FieldDefinition f) =>
        (f.Options ?? Array.Empty<LText>() as IReadOnlyList<LText>).Select(o => o.ToString()).ToHashSet();

    public static IReadOnlyList<string> SplitMulti(string value)
    {
        if (value.StartsWith('['))
        {
            try { return JsonSerializer.Deserialize<List<string>>(value) ?? new List<string>(); } catch (JsonException) { /* falls through */ }
        }
        return value.Split(", ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    public static bool IsTrue(string v) => v is "true" or "on" or "1" or "yes" or "ja";

    private static bool LooksLikeEmail(string v)
    {
        var at = v.IndexOf('@');
        return v.Length <= 254 && at > 0 && at < v.Length - 3 && v.IndexOf('@', at + 1) < 0 && v[(at + 1)..].Contains('.') && !v.Any(char.IsWhiteSpace);
    }
}
