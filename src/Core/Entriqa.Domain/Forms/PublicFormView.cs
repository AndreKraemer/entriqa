using System.Security.Cryptography;
using System.Text;
using Entriqa.Domain.Validation;

namespace Entriqa.Domain.Forms;

/// <summary>
/// What the browser gets: fields, texts, quiz questions with their jump targets - but no pipeline,
/// no points, no result or finding texts. Scoring happens on the server only.
/// Always single-language: <see cref="From"/> expects an already localized definition;
/// <c>Strings</c> supplies the UI and error texts of that same language for forms.js.
/// </summary>
public sealed record PublicFormView(
    string Slug,
    string Name,
    string Type,
    int Version,
    string Lang,
    string? Intro,
    string? SubmitLabel,
    IReadOnlyList<PublicFieldView> Fields,
    PublicQuizView? Quiz,
    PublicCompletionView Completion,
    IReadOnlyDictionary<string, string> Strings)
{
    /// <summary>
    /// <paramref name="offered"/> are the appointments on offer right now (#7), already filtered by the caller;
    /// they go out on the appointment field as UTC times, which forms.js shows in the visitor's own zone.
    /// </summary>
    public static PublicFormView From(FormDefinition d, int version, string lang, IReadOnlyList<Appointment>? offered = null) => new(
        d.Slug, d.Name, d.Type, version, lang,
        d.Intro?.ToString(), d.SubmitLabel?.ToString(),
        d.Fields.Select(f => PublicFieldView.From(f, offered)).ToList(),
        d.Quiz is null ? null : new PublicQuizView(
            d.Quiz.CollectEmail,
            d.Quiz.Questions.Select(q => new PublicQuizQuestion(q.Id, q.Text.ToString(),
                q.Options.Select(o => new PublicQuizOption(o.Id, o.Label.ToString(), o.Next)).ToList())).ToList()),
        new PublicCompletionView(d.Completion.Mode, d.Completion.Message?.ToString(), d.Completion.Url?.ToString()),
        ValidationMessages.For(lang));

    /// <summary>
    /// The HTTP cache validator of this view (#7). The version alone is not enough once appointments are on
    /// offer: they change without a new version, and a 304 on the old tag would keep a stale list alive.
    /// </summary>
    public string CacheTag()
    {
        if (!Fields.Any(f => f.Type == FieldTypes.Appointment)) return $"\"v{Version}-{Lang}\"";
        var offer = Fields.SelectMany(f => f.Appointments ?? [])
            .Select(a => $"{a.Id}|{a.Start.UtcTicks}|{a.End?.UtcTicks}|{a.Title}");
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', offer)));
        return $"\"v{Version}-{Lang}-{Convert.ToHexStringLower(hash.AsSpan(0, 6))}\"";
    }
}

public sealed record PublicFieldView(
    string Id, string Type, string Label, bool Required, string? Placeholder, string? Help,
    IReadOnlyList<string>? Options, string? Text, string? Source, int? MaxLength, decimal? Min, decimal? Max, bool BusinessOnly,
    PublicVisibleIf? VisibleIf, IReadOnlyList<PublicAppointmentOption>? Appointments = null)
{
    public static PublicFieldView From(FieldDefinition f, IReadOnlyList<Appointment>? offered = null) => new(
        f.Id, f.Type, f.Label.ToString(), f.Required || f.Type == FieldTypes.Appointment, f.Placeholder?.ToString(), f.Help?.ToString(),
        f.Options?.Select(o => o.ToString()).ToList(), f.Text?.ToString(), f.Source, f.MaxLength, f.Min, f.Max, f.BusinessOnly,
        f.VisibleIf is null ? null : new PublicVisibleIf(f.VisibleIf.Field, f.VisibleIf.Options),
        f.Type == FieldTypes.Appointment
            ? (offered ?? []).Select(a => new PublicAppointmentOption(a.Id, a.Start.ToUniversalTime(), a.End?.ToUniversalTime(), a.Title)).ToList()
            : null);
}

/// <summary>One appointment on offer (#7). Times are UTC; the browser formats them in the visitor's zone.</summary>
public sealed record PublicAppointmentOption(string Id, DateTimeOffset Start, DateTimeOffset? End, string? Title);

public sealed record PublicVisibleIf(string Field, IReadOnlyList<int>? Options);

public sealed record PublicCompletionView(string Mode, string? Message, string? Url);
public sealed record PublicQuizView(string CollectEmail, IReadOnlyList<PublicQuizQuestion> Questions);
public sealed record PublicQuizQuestion(string Id, string Text, IReadOnlyList<PublicQuizOption> Options);
public sealed record PublicQuizOption(string Id, string Label, string? Next);
