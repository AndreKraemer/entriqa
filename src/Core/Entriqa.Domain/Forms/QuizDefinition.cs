namespace Entriqa.Domain.Forms;

public sealed record QuizDefinition(
    string Scoring,                                 // sum (normalized to the path taken) | category
    string CollectEmail,                            // none | optional | required
    IReadOnlyList<QuizQuestion> Questions,
    IReadOnlyList<QuizResult> Results,
    QuizFindings? Findings = null)                  // individual evaluation texts (self-assessment pattern)
{
    public QuizDefinition Localize(string lang) => this with
    {
        Questions = Questions.Select(q => q with
        {
            Text = q.Text.Localized(lang),
            Topic = q.Topic?.Localized(lang),
            Options = q.Options.Select(o => o with { Label = o.Label.Localized(lang), Finding = o.Finding?.Localized(lang) }).ToList(),
        }).ToList(),
        Results = Results.Select(r => r with { Title = r.Title.Localized(lang), Body = r.Body.Localized(lang) }).ToList(),
        Findings = Findings is null ? null : Findings with
        {
            WarningTemplate = Findings.WarningTemplate?.Localized(lang),
            EmptyText = Findings.EmptyText?.Localized(lang),
        },
    };
}

public sealed record QuizQuestion(string Id, LText Text, IReadOnlyList<QuizOption> Options, LText? Topic = null);

/// <summary><c>Next</c>: null = next question in order, question id = jump, "result:{id}" = straight to the result.
/// <c>Finding</c>: individual evaluation text shown when this option was chosen (typically on the highest score).</summary>
public sealed record QuizOption(string Id, LText Label, int Points, string? Next = null, string? Category = null, LText? Finding = null);

public sealed record QuizResult(string Id, int MinPct, int MaxPct, LText Title, LText Body);

/// <summary>
/// Selection rules for the findings: up to <c>Max</c> texts of the chosen options, ordered by <c>Priority</c>
/// (question ids; unlisted ones follow in path order). With fewer than two, <c>WarningTemplate</c>
/// ({topic} placeholder) fills up from the one-point topics; without any hit at all <c>EmptyText</c> applies.
/// </summary>
public sealed record QuizFindings(
    int Max = 3,
    IReadOnlyList<string>? Priority = null,
    LText? WarningTemplate = null,
    LText? EmptyText = null);
