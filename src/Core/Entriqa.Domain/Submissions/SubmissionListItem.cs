namespace Entriqa.Domain.Submissions;

public sealed record SubmissionListItem(
    string Id, string Slug, int Version, DateTimeOffset CreatedAt, string? Email, string Summary,
    SubmissionState State, string Handling, string? QuizResultId, string? Assignee = null,
    // #15: ExpiresAt is SubmissionRetention.EffectiveExpiry(...); DateTimeOffset.MaxValue there means
    // RetainedIndefinitely, so a caller only needs the flag first and never compares against MaxValue itself.
    DateTimeOffset ExpiresAt = default, bool RetainedIndefinitely = false);

public sealed record SubmissionPage(IReadOnlyList<SubmissionListItem> Items, string? ContinuationToken);

/// <summary>
/// One submission as a search sees it (#12): the row the list would show, plus every text a term may
/// match. Which texts those are is the data layer's answer - the use case only asks whether one of
/// them contains the term, so the rule stays in one executable place.
/// </summary>
public sealed record SubmissionCandidate(SubmissionListItem Item, IReadOnlyList<string> Texts);

/// <summary>
/// What one scan of the submission table yielded (#12): the candidates, how many rows were looked at,
/// and whether the ceiling stopped the scan before the table ended - which is the fact AC6 forbids
/// showing a result without.
/// </summary>
public sealed record SubmissionCandidates(IReadOnlyList<SubmissionCandidate> Items, int Scanned, bool Capped);
