namespace Entriqa.Domain.Submissions;

public enum StepRunStatus { Pending, Ok, Waiting, Failed, Blocked, Skipped }
public enum StepPhase { OnSubmit, OnConfirm }

public sealed class StepRun
{
    public required string StepId { get; init; }
    public required string StepKey { get; init; }
    public required StepPhase Phase { get; init; }
    public StepRunStatus Status { get; set; } = StepRunStatus.Pending;
    public string? Error { get; set; }
    public int Attempts { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
}
