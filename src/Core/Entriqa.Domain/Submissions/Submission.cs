using Entriqa.Domain.Quiz;

namespace Entriqa.Domain.Submissions;

/// <summary>
/// The "submission" aggregate: values, quiz outcome, step runs and artifacts.
/// Stored as a whole (one upsert = one transaction, §14.2 of the Solution Standard).
/// </summary>
public sealed class Submission
{
    public required string Id { get; init; }                       // "{slug}:{rowKey}"
    public required string Slug { get; init; }
    public required int Version { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required Dictionary<string, string> Values { get; init; } // field id -> value (multi-select: separated by ", ")
    public string? Email { get; init; }
    public string? FirstName { get; init; }
    public string? Source { get; init; }                           // utm_source or similar, taken from hidden fields
    public string? Locale { get; init; }                           // language of the submission (mails, PDF, deferred runs)
    public string? IpHash { get; init; }                           // only for rate limiting and DOI evidence
    public QuizOutcome? Quiz { get; init; }
    public string? ConsentText { get; init; }                      // the exact text at the time of submission
    public List<StepRun> StepRuns { get; init; } = new();
    public Dictionary<string, string> Artifacts { get; init; } = new(); // "report" -> blob path, "download" -> URL
    public string Handling { get; set; } = HandlingStates.None;
    public string? Assignee { get; set; }                          // #13: who takes care of it; null = nobody
    public DateTimeOffset? ConfirmedAt { get; set; }
    public string? ConfirmedIpHash { get; set; }
    public string? BrevoContactId { get; set; }
    public string? ETag { get; set; }                              // optimistic concurrency; set by the data layer

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
