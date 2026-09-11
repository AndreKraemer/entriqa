namespace Entriqa.Domain.Submissions;

/// <summary>What a history entry is about (#14). Stable keys, never prose: the admin builds the German
/// wording from them, so a stored sentence would be frozen and untranslatable.</summary>
public static class HistoryTypes
{
    public const string Handling = "handling";      // Detail: the new handling state
    public const string Assignee = "assignee";      // Detail: who it went to, null = nobody
    public const string StepRetry = "step.retry";   // Detail: the step id, null = every failed step
    public const string DoiResend = "doi.resend";   // Detail: none
}

/// <summary>Where an entry came from. See <see cref="HistoryActor"/> for why these are two values, not three.</summary>
public static class HistoryOrigins
{
    public const string Admin = "admin", System = "system";
}

/// <summary>
/// The originator of a history entry (#14, AC4). Two fields carry three states, and that is the point:
/// an admin by name, an admin whose request carried no identity (<see cref="Admin"/> with nothing), and
/// the system itself. <see cref="System"/> can never be given a name, so "what the system did is never
/// attributed to a person" holds by construction rather than by care - and an unidentified human is
/// still a human, not an automatic run.
/// </summary>
public readonly record struct HistoryActor(string Origin, string? By)
{
    /// <summary>An admin acting. A blank name means the request carried no identity - not an admin called "".</summary>
    public static HistoryActor Admin(string? name) => throw new NotImplementedException();

    /// <summary>Housekeeping and anything else nobody clicked.</summary>
    public static HistoryActor System => throw new NotImplementedException();

    public bool IsAutomatic => Origin == HistoryOrigins.System;
}

/// <summary>
/// One thing that happened to a submission. Immutable on purpose (AC6): every member is init-only, so
/// an entry that exists can be read and appended to, never rewritten.
/// </summary>
public sealed record SubmissionHistoryEntry
{
    public required DateTimeOffset At { get; init; }
    public required string Type { get; init; }
    public required string Origin { get; init; }
    public string? By { get; init; }
    public string? Detail { get; init; }
}

/// <summary>
/// The submission's own history (#14). Append-only: there is no way in here that replaces or removes a
/// single entry (AC6), and it lives inside the submission row, so it is born and dies with it (AC7) -
/// deliberately the opposite decision from the consent proofs of #1, which have to outlive it.
///
/// <para><see cref="Max"/> exists because an Azure Table string property ends at 32768 characters. An
/// unbounded history would eventually make the submission unsaveable, which is a worse failure than a
/// shortened one; the oldest entries give way wholesale, which is not the individual deletion AC6
/// forbids.</para>
/// </summary>
public sealed class SubmissionHistory
{
    public const int Max = 200;

    private readonly List<SubmissionHistoryEntry> _entries;

    public SubmissionHistory() => _entries = [];

    /// <summary>Rehydration from storage - the data layer's way back in.</summary>
    public SubmissionHistory(IEnumerable<SubmissionHistoryEntry> stored) => _entries = [.. stored];

    /// <summary>Oldest first, the order they were appended in. The detail view reverses it (AC5).</summary>
    public IReadOnlyList<SubmissionHistoryEntry> Entries => _entries;

    public void Append(SubmissionHistoryEntry entry) => throw new NotImplementedException();
}
