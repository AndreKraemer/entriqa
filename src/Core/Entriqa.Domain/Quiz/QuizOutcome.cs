namespace Entriqa.Domain.Quiz;

public sealed record QuizOutcome(
    Dictionary<string, string> Answers,                    // only questions on the path actually taken
    IReadOnlyList<string> Path,
    int Points,
    int MaxPoints,                                         // maximum reachable on this path
    int Pct,
    string ResultId,
    bool ReachedByJump,
    IReadOnlyList<string>? Findings = null);               // resolved evaluation texts (language of the submission)
