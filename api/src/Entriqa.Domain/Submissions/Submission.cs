using Entriqa.Domain.Quiz;

namespace Entriqa.Domain.Submissions;

/// <summary>
/// Das Aggregat "Einsendung": Werte, Quiz-Ergebnis, Schrittläufe und Artefakte.
/// Wird als Ganzes gespeichert (ein Upsert = eine Transaktion, §14.2 des Solution Standards).
/// </summary>
public sealed class Submission
{
    public required string Id { get; init; }                       // "{slug}:{rowKey}"
    public required string Slug { get; init; }
    public required int Version { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required Dictionary<string, string> Values { get; init; } // Feld-ID → Wert (Mehrfachauswahl: durch ", " getrennt)
    public string? Email { get; init; }
    public string? FirstName { get; init; }
    public string? Source { get; init; }                           // utm_source o. ä., aus hidden-Feldern
    public string? Locale { get; init; }                           // Sprache der Einsendung (Mails, PDF, Deferred-Läufe)
    public string? IpHash { get; init; }                           // nur für Rate-Limit und DOI-Nachweis
    public QuizOutcome? Quiz { get; init; }
    public string? ConsentText { get; init; }                      // exakter Text zum Zeitpunkt der Einsendung
    public List<StepRun> StepRuns { get; init; } = new();
    public Dictionary<string, string> Artifacts { get; init; } = new(); // "report" → Blob-Pfad, "download" → URL
    public string Handling { get; set; } = HandlingStates.None;
    public DateTimeOffset? ConfirmedAt { get; set; }
    public string? ConfirmedIpHash { get; set; }
    public string? BrevoContactId { get; set; }
    public string? ETag { get; set; }                              // optimistische Nebenläufigkeit; gesetzt von der Data-Schicht

    public bool IsConfirmed => ConfirmedAt.HasValue;
    public bool HasEmail => !string.IsNullOrWhiteSpace(Email);

    public StepRun Run(string stepId) => StepRuns.First(r => r.StepId == stepId);

    public SubmissionState State
    {
        get
        {
            if (StepRuns.Any(r => r.Status == StepRunStatus.Failed)) return SubmissionState.Failed;
            if (StepRuns.Any(r => r.Status is StepRunStatus.Waiting)) return SubmissionState.AwaitingConfirmation;
            if (StepRuns.Any(r => r.Status is StepRunStatus.Pending)) return SubmissionState.Processing;
            return SubmissionState.Done;
        }
    }
}

public enum SubmissionState { Processing, AwaitingConfirmation, Failed, Done }

public static class HandlingStates
{
    public const string None = "none", Open = "open", Done = "done";
}
