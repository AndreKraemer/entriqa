namespace Entriqa.Domain.Submissions;

/// <summary>Was forms.js an POST /api/forms/{slug}/submissions schickt.</summary>
public sealed record SubmitFormRequest(
    string Slug,
    string Token,                                          // Anti-Spam-Token von GET /api/forms/{slug}/token
    Dictionary<string, string> Values,                     // Feld-ID → Wert; Mehrfachauswahl als JSON-Array-String oder ", "-Liste
    Dictionary<string, string>? Answers,                   // Quiz: Fragen-ID → Options-ID
    string? Honeypot,                                      // Feld "website" – muss leer sein
    string? ClientIp,                                      // setzt die Function aus dem Request, nie der Client
    string? Lang = null);                                  // Sprache der Einbauseite (data-lang); unbekannt → Standard-Locale

public sealed record SubmitFormResult(
    string SubmissionId,
    CompletionView Completion,
    QuizResultView? Quiz,
    string? RunToken);                                     // gesetzt, wenn Hintergrund-Schritte ausstehen

public sealed record CompletionView(string Mode, string? Message, string? Url);
public sealed record QuizResultView(string ResultId, string Title, string Body, int Points, int MaxPoints, int Pct,
    IReadOnlyList<string>? Findings = null);
