namespace Entriqa.Domain.Quiz;

public sealed record QuizOutcome(
    Dictionary<string, string> Answers,                    // nur Fragen auf dem tatsächlich gegangenen Pfad
    IReadOnlyList<string> Path,
    int Points,
    int MaxPoints,                                         // Maximum auf diesem Pfad
    int Pct,
    string ResultId,
    bool ReachedByJump,
    IReadOnlyList<string>? Findings = null);               // aufgelöste Auswertungstexte (Sprache der Einsendung)
