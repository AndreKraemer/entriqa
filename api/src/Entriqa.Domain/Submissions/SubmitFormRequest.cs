namespace Entriqa.Domain.Submissions;

/// <summary>What forms.js posts to POST /api/forms/{slug}/submissions.</summary>
public sealed record SubmitFormRequest(
    string Slug,
    string Token,                                          // anti-spam token from GET /api/forms/{slug}/token
    Dictionary<string, string> Values,                     // field id -> value; multi-select as a JSON array string or a ", " list
    Dictionary<string, string>? Answers,                   // quiz: question id -> option id
    string? Honeypot,                                      // field "website" - has to be empty
    string? ClientIp,                                      // set by the function from the request, never by the client
    string? Lang = null);                                  // language of the hosting page (data-lang); unknown -> default locale

public sealed record SubmitFormResult(
    string SubmissionId,
    CompletionView Completion,
    QuizResultView? Quiz,
    string? RunToken);                                     // set when background steps are still pending

public sealed record CompletionView(string Mode, string? Message, string? Url);
public sealed record QuizResultView(string ResultId, string Title, string Body, int Points, int MaxPoints, int Pct,
    IReadOnlyList<string>? Findings = null);
