namespace Entriqa.Domain.Submissions;

public sealed record SubmissionListItem(
    string Id, string Slug, int Version, DateTimeOffset CreatedAt, string? Email, string Summary,
    SubmissionState State, string Handling, string? QuizResultId);

public sealed record SubmissionPage(IReadOnlyList<SubmissionListItem> Items, string? ContinuationToken);
