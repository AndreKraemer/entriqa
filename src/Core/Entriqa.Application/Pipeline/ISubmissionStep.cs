using System.Text.Json;
using Entriqa.Domain.Forms;
using Entriqa.Domain.Submissions;

namespace Entriqa.Application.Pipeline;

public enum StepMode { Inline, Deferred }

/// <summary>
/// How a step behaves in a test run (#21). <see cref="Suppressed"/> is the default and the safe one:
/// a step never performs its external write in a test unless it explicitly opts into <see cref="Redirected"/>.
/// That is what keeps AC5 ("never a Brevo contact, webhook or Teams message") a structural guarantee -
/// a step added later is suppressed until someone decides otherwise.
/// </summary>
public enum StepTestBehavior
{
    Suppressed,     // not executed; DescribeTest reports what it would have done
    Redirected      // executed, but mail goes to the signed-in admin (mail steps only)
}

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

    /// <summary>How this step behaves in a test run (#21). Suppressed by default - see <see cref="StepTestBehavior"/>.</summary>
    StepTestBehavior TestBehavior => StepTestBehavior.Suppressed;

    /// <summary>
    /// For a suppressed step (#21): the resolved values it would have used, for the test protocol - the actual
    /// list, template, file or target address, resolved for the submission's locale. Empty by default.
    /// </summary>
    IReadOnlyList<Entriqa.Domain.UseCases.TestNote> DescribeTest(StepContext ctx, JsonElement config) => Array.Empty<Entriqa.Domain.UseCases.TestNote>();

    /// <summary>Checks the configuration when publishing. Returns problems in plain words.</summary>
    IEnumerable<string> CheckConfig(JsonElement config, FormDefinition form, IReadOnlySet<string> producedBefore) => Array.Empty<string>();

    Task<StepResult> ExecuteAsync(StepContext ctx, JsonElement config, CancellationToken ct);
}

public enum StepNeed { EmailField, ConsentField }

/// <summary>Carried on <see cref="StepContext.Test"/> during a test run (#21). <see cref="MailTo"/> is the
/// signed-in admin's address, or null when the principal carried no usable address.</summary>
public sealed record TestRun(string? MailTo);

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

    /// <summary>Non-null in a test run (#21): mail steps send to <see cref="TestRun.MailTo"/>, not the visitor.</summary>
    public TestRun? Test { get; init; }

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

    // The locale-aware half, for the fields marked "localizable" in a step's ConfigSchema. A value there
    // is a plain scalar (applies to every language) or {locale: value} - see LValue. The plain overloads
    // above stay for everything that is not visitor facing (attach, hours, to, webhookUrl, ...); reading a
    // marked field through them returns null for the object form, which is why they must not be used there.
    public static string? GetString(this JsonElement e, string name, string? locale)
        => Localized(e, name, locale) is { ValueKind: JsonValueKind.String } p ? p.GetString() : null;

    public static int? GetInt(this JsonElement e, string name, string? locale)
        => Localized(e, name, locale) is { ValueKind: JsonValueKind.Number } p && p.TryGetInt32(out var i) ? i : null;

    public static IReadOnlyList<int> GetIntList(this JsonElement e, string name, string? locale)
        => Localized(e, name, locale) is { ValueKind: JsonValueKind.Array } p
            ? p.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.Number && x.TryGetInt32(out _)).Select(x => x.GetInt32()).ToList()
            : Array.Empty<int>();

    /// <summary>Reads a map whose <em>members</em> are localizable (<c>reportingcloud.pdf</c>'s per-result
    /// templates): the outer keys stay what they were, each value is resolved for the language.</summary>
    public static Dictionary<string, string> GetStringMap(this JsonElement e, string name, string? locale)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(name, out var p) || p.ValueKind != JsonValueKind.Object) return map;
        foreach (var member in p.EnumerateObject())
            if (LValue.Resolve(member.Value, locale) is { ValueKind: JsonValueKind.String } v) map[member.Name] = v.GetString()!;
        return map;
    }

    /// <summary>
    /// A property as it is stored, without resolving anything - for a map whose <em>members</em> are the
    /// localizable leaves, where resolving the map itself would hand back an arbitrary member. Returns
    /// <see cref="JsonValueKind.Undefined"/> when the property is absent.
    /// </summary>
    public static JsonElement GetRaw(this JsonElement e, string name)
        => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var p) ? p : default;

    /// <summary>The value of a localizable property, reduced to one language.</summary>
    private static JsonElement Localized(JsonElement e, string name, string? locale)
        => LValue.Resolve(GetRaw(e, name), locale);
}
