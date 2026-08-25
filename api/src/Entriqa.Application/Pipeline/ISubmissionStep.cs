using System.Text.Json;
using Entriqa.Domain.Forms;
using Entriqa.Domain.Submissions;

namespace Entriqa.Application.Pipeline;

public enum StepMode { Inline, Deferred }

/// <summary>
/// One post-processing step. Registered via DI (suffix "Step"); the admin reads the catalog through
/// <see cref="StepCatalogService"/>. Neue Schritte: Klasse anlegen – fertig.
/// </summary>
public interface ISubmissionStep
{
    string Key { get; }                             // "brevo.mail" - stable, appears in the form definition
    string Name { get; }
    string Description { get; }
    StepMode Mode { get; }                          // Inline: inside the request; Deferred: after the response
    bool SplitsPhase => false;                      // true for doi.request only: everything after it runs once confirmed
    bool CriticalByDefault => true;                 // false for notifications: a failure does not block the following steps
    IReadOnlyList<StepNeed> Needs => Array.Empty<StepNeed>();
    string? Produces => null;                       // "report", "download"
    string ConfigSchema { get; }                    // JSON Schema for the builder
    /// <summary>Variables the step passes to Brevo templates ({{ params.… }}) - the admin shows them as help.</summary>
    IReadOnlyList<Entriqa.Domain.UseCases.MailParam> MailParams => Array.Empty<Entriqa.Domain.UseCases.MailParam>();

    /// <summary>Checks the configuration when publishing. Returns problems in plain words.</summary>
    IEnumerable<string> CheckConfig(JsonElement config, FormDefinition form, IReadOnlySet<string> producedBefore) => Array.Empty<string>();

    Task<StepResult> ExecuteAsync(StepContext ctx, JsonElement config, CancellationToken ct);
}

public enum StepNeed { EmailField, ConsentField }

public sealed record StepResult(StepRunStatus Status, string? Error = null)
{
    public static readonly StepResult Ok = new(StepRunStatus.Ok);
    public static StepResult Failed(string error) => new(StepRunStatus.Failed, error);
}

/// <summary>What a step sees and where it puts results (artifacts end up in the submission).</summary>
public sealed class StepContext
{
    public required Submission Submission { get; init; }
    public required FormDefinition Form { get; init; }
    public required int FormVersion { get; init; }
    public required EntriqaOptions Options { get; init; }

    public Dictionary<string, string> Artifacts => Submission.Artifacts;
    public string? Email => Submission.Email;
    public string? FirstName => Submission.FirstName;
    public QuizResult? QuizResult => Submission.Quiz is null ? null : Form.Quiz?.Results.FirstOrDefault(r => r.Id == Submission.Quiz.ResultId);

    /// <summary>Field values keyed by label - for mail parameters and merge data.</summary>
    public Dictionary<string, object?> LabeledValues()
    {
        var d = new Dictionary<string, object?>();
        foreach (var f in Form.Fields)
            if (!FieldTypes.IsLayout(f.Type) && Submission.Values.TryGetValue(f.Id, out var v)) d[f.Label.ToString()] = v;
        return d;
    }
}

/// <summary>Small helpers for reading the step configuration.</summary>
public static class StepConfig
{
    public static string? GetString(this JsonElement e, string name)
        => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
    public static int? GetInt(this JsonElement e, string name)
        => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var p)
           && p.ValueKind == JsonValueKind.Number && p.TryGetInt32(out var i) ? i : null;
    public static IReadOnlyList<int> GetIntList(this JsonElement e, string name)
        => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Array
            ? p.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.Number && x.TryGetInt32(out _)).Select(x => x.GetInt32()).ToList() : Array.Empty<int>();
    public static Dictionary<string, string> GetStringMap(this JsonElement e, string name)
        => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Object
            ? p.EnumerateObject().Where(x => x.Value.ValueKind == JsonValueKind.String).ToDictionary(x => x.Name, x => x.Value.GetString()!) : new();
}
