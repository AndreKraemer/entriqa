using System.Text.Json;
using Entriqa.Data.Entities;
using Entriqa.Domain.Quiz;
using Entriqa.Domain.Submissions;

namespace Entriqa.Data.Mapping;

/// <summary>The customs post between domain and entity. Hand written on purpose: few fields, JSON columns.</summary>
internal static class SubmissionMapper
{
    public static SubmissionEntity ToEntity(Submission s)
    {
        var sep = s.Id.IndexOf(':');
        return new SubmissionEntity
        {
            PartitionKey = s.Id[..sep],
            RowKey = s.Id[(sep + 1)..],
            Version = s.Version,
            CreatedAt = s.CreatedAt,
            Email = s.Email,
            FirstName = s.FirstName,
            Source = s.Source,
            Locale = s.Locale,
            IpHash = s.IpHash,
            ValuesJson = JsonSerializer.Serialize(s.Values, TableStorage.Json),
            QuizJson = s.Quiz is null ? null : JsonSerializer.Serialize(s.Quiz, TableStorage.Json),
            ConsentText = s.ConsentText,
            StepRunsJson = JsonSerializer.Serialize(s.StepRuns, TableStorage.Json),
            HistoryJson = JsonSerializer.Serialize(s.History.Entries, TableStorage.Json),
            ArtifactsJson = JsonSerializer.Serialize(s.Artifacts, TableStorage.Json),
            Handling = s.Handling,
            State = s.State.ToString().ToLowerInvariant(),
            QuizResultId = s.Quiz?.ResultId,
            Assignee = s.Assignee,
            ConfirmedAt = s.ConfirmedAt,
            ConfirmedIpHash = s.ConfirmedIpHash,
            BrevoContactId = s.BrevoContactId,
        };
    }

    public static Submission ToDomain(SubmissionEntity e) => new()
    {
        Id = $"{e.PartitionKey}:{e.RowKey}",
        Slug = e.PartitionKey,
        Version = e.Version,
        CreatedAt = e.CreatedAt,
        Email = e.Email,
        FirstName = e.FirstName,
        Source = e.Source,
        Locale = e.Locale,
        IpHash = e.IpHash,
        Values = JsonSerializer.Deserialize<Dictionary<string, string>>(e.ValuesJson, TableStorage.Json) ?? new(),
        Quiz = e.QuizJson is null ? null : JsonSerializer.Deserialize<QuizOutcome>(e.QuizJson, TableStorage.Json),
        ConsentText = e.ConsentText,
        StepRuns = JsonSerializer.Deserialize<List<StepRun>>(e.StepRunsJson, TableStorage.Json) ?? new(),
        History = new SubmissionHistory(JsonSerializer.Deserialize<List<SubmissionHistoryEntry>>(e.HistoryJson, TableStorage.Json) ?? new()),
        Artifacts = JsonSerializer.Deserialize<Dictionary<string, string>>(e.ArtifactsJson, TableStorage.Json) ?? new(),
        Handling = e.Handling,
        Assignee = e.Assignee,
        ConfirmedAt = e.ConfirmedAt,
        ConfirmedIpHash = e.ConfirmedIpHash,
        BrevoContactId = e.BrevoContactId,
        ETag = e.ETag.ToString() is { Length: > 0 } tag ? tag : null,
    };

    public static SubmissionListItem ToListItem(SubmissionEntity e)
    {
        var values = JsonSerializer.Deserialize<Dictionary<string, string>>(e.ValuesJson, TableStorage.Json) ?? new();
        var summary = e.Email ?? values.Values.FirstOrDefault() ?? "";
        if (!Enum.TryParse<SubmissionState>(e.State, true, out var state)) state = SubmissionState.Processing;
        return new SubmissionListItem($"{e.PartitionKey}:{e.RowKey}", e.PartitionKey, e.Version, e.CreatedAt, e.Email, summary,
            state, e.Handling, e.QuizResultId, e.Assignee);
    }
}
