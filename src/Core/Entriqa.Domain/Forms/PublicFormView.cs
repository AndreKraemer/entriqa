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
    public static PublicFormView From(FormDefinition d, int version, string lang) => new(
        d.Slug, d.Name, d.Type, version, lang,
        d.Intro?.ToString(), d.SubmitLabel?.ToString(),
        d.Fields.Select(PublicFieldView.From).ToList(),
        d.Quiz is null ? null : new PublicQuizView(
            d.Quiz.CollectEmail,
            d.Quiz.Questions.Select(q => new PublicQuizQuestion(q.Id, q.Text.ToString(),
                q.Options.Select(o => new PublicQuizOption(o.Id, o.Label.ToString(), o.Next)).ToList())).ToList()),
        new PublicCompletionView(d.Completion.Mode, d.Completion.Message?.ToString(), d.Completion.Url?.ToString()),
        ValidationMessages.For(lang));
}

public sealed record PublicFieldView(
    string Id, string Type, string Label, bool Required, string? Placeholder, string? Help,
    IReadOnlyList<string>? Options, string? Text, string? Source, int? MaxLength, decimal? Min, decimal? Max, bool BusinessOnly,
    PublicVisibleIf? VisibleIf)
{
    public static PublicFieldView From(FieldDefinition f) => new(
        f.Id, f.Type, f.Label.ToString(), f.Required, f.Placeholder?.ToString(), f.Help?.ToString(),
        f.Options?.Select(o => o.ToString()).ToList(), f.Text?.ToString(), f.Source, f.MaxLength, f.Min, f.Max, f.BusinessOnly,
        f.VisibleIf is null ? null : new PublicVisibleIf(f.VisibleIf.Field, f.VisibleIf.Options));
}

public sealed record PublicVisibleIf(string Field, IReadOnlyList<int>? Options);

public sealed record PublicCompletionView(string Mode, string? Message, string? Url);
public sealed record PublicQuizView(string CollectEmail, IReadOnlyList<PublicQuizQuestion> Questions);
public sealed record PublicQuizQuestion(string Id, string Text, IReadOnlyList<PublicQuizOption> Options);
public sealed record PublicQuizOption(string Id, string Label, string? Next);
