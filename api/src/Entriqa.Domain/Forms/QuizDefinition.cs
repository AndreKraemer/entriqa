namespace Entriqa.Domain.Forms;

public sealed record QuizDefinition(
    string Scoring,                                 // sum (normiert auf den gegangenen Pfad) | category
    string CollectEmail,                            // none | optional | required
    IReadOnlyList<QuizQuestion> Questions,
    IReadOnlyList<QuizResult> Results,
    QuizFindings? Findings = null)                  // individuelle Auswertungstexte (Selbsttest-Muster)
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

/// <summary><c>Next</c>: null = nächste Frage in der Reihenfolge, Fragen-ID = Sprung, "result:{id}" = direkt zum Ergebnis.
/// <c>Finding</c>: individueller Auswertungstext, wenn diese Option gewählt wurde (typisch auf der höchsten Punktzahl).</summary>
public sealed record QuizOption(string Id, LText Label, int Points, string? Next = null, string? Category = null, LText? Finding = null);

public sealed record QuizResult(string Id, int MinPct, int MaxPct, LText Title, LText Body);

/// <summary>
/// Auswahlregeln für die Findings: bis zu <c>Max</c> Texte der gewählten Optionen, geordnet nach <c>Priority</c>
/// (Fragen-IDs; nicht gelistete folgen in Pfadreihenfolge). Bei weniger als zwei füllt <c>WarningTemplate</c>
/// ({topic}-Platzhalter) über die 1-Punkt-Themen auf; ganz ohne Treffer greift <c>EmptyText</c>.
/// </summary>
public sealed record QuizFindings(
    int Max = 3,
    IReadOnlyList<string>? Priority = null,
    LText? WarningTemplate = null,
    LText? EmptyText = null);
