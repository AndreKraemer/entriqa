using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Entriqa.Domain.Errors;
using Entriqa.Domain.Forms;
using Entriqa.Domain.Submissions;

namespace Entriqa.Application.Pipeline;

public enum RunMode
{
    Inline,     // in the submit request: inline steps only, deferred ones stay pending
    Deferred,   // background run resp. after confirmation: everything that can run
    Retry       // like Deferred, but resets failed and blocked steps first
}

/// <summary>
/// The runner: strictly sequential, one failure blocks the following steps, every step keeps its own status.
/// Not a use case (no user operation) but a domain service per Solution Standard §13.5.
/// </summary>
public sealed class SubmissionPipelineService(
    IEnumerable<ISubmissionStep> steps,
    IOptions<EntriqaOptions> options,
    TimeProvider time,
    ILogger<SubmissionPipelineService> log)
{
    private readonly Dictionary<string, ISubmissionStep> _steps = steps.ToDictionary(s => s.Key);

    public ISubmissionStep Resolve(string key) =>
        _steps.TryGetValue(key, out var s) ? s : throw new AppException(ErrorCodes.StepUnknown, ErrorMessages.StepUnknown, AppException.Args("key", key), 500);

    /// <summary>For the check rules: an unknown key is an issue there, not a server error.</summary>
    public ISubmissionStep? TryResolve(string key) => _steps.GetValueOrDefault(key);

    /// <summary>Creates the step runs when the submission is created (phase from the position relative to the splitting step).</summary>
    public List<StepRun> CreateRuns(FormDefinition form)
    {
        var phase = StepPhase.OnSubmit;
        var runs = new List<StepRun>();
        foreach (var st in form.Pipeline)
        {
            runs.Add(new StepRun { StepId = st.Id, StepKey = st.Step, Phase = phase });
            if (Resolve(st.Step).SplitsPhase) phase = StepPhase.OnConfirm;
        }
        return runs;
    }

    /// <summary>Returns true when deferred steps are still pending afterwards (-> run token for the client).</summary>
    /// <param name="repeatedBy">
    /// Who asked for a repeat (#14): an admin, or <see cref="HistoryActor.System"/> for housekeeping's
    /// auto retry. Only consulted in <see cref="RunMode.Retry"/> - a first run is not a repeat and writes
    /// no entry. This is the one place both halves of AC3/AC4 pass through.
    /// </param>
    public async Task<bool> RunAsync(Submission submission, FormDefinition form, int version, RunMode mode, string? onlyStepId = null, HistoryActor? repeatedBy = null, CancellationToken ct = default)
    {
        if (mode == RunMode.Retry)
        {
            foreach (var r in submission.StepRuns.Where(r => r.Status is StepRunStatus.Failed or StepRunStatus.Blocked && (onlyStepId is null || r.StepId == onlyStepId || r.Status == StepRunStatus.Blocked)))
            { r.Status = StepRunStatus.Pending; r.Error = null; }
            if (repeatedBy is { } by) submission.Record(HistoryTypes.StepRetry, by, time.GetUtcNow(), onlyStepId);
        }

        var ctx = new StepContext { Submission = submission, Form = form, FormVersion = version, Options = options.Value };
        var broken = false;
        var deferredLeft = false;

        foreach (var def in form.Pipeline)
        {
            var run = submission.Run(def.Id);
            var step = Resolve(def.Step);

            var critical = def.Critical ?? step.CriticalByDefault;
            if (run.Status is StepRunStatus.Ok or StepRunStatus.Skipped) continue;
            if (run.Status == StepRunStatus.Failed) { if (critical) broken = true; continue; }
            if (broken) { run.Status = StepRunStatus.Blocked; continue; }

            if (!ConditionMet(def.When, submission)) { run.Status = StepRunStatus.Skipped; continue; }
            if (run.Phase == StepPhase.OnConfirm && !submission.IsConfirmed) { run.Status = StepRunStatus.Waiting; continue; }
            if (mode == RunMode.Inline && step.Mode == StepMode.Deferred)
            {
                // A deferred step splits the run (the way doi.request splits the phases): everything from here on runs
                // in the deferred run only. Otherwise an inline consumer would run before its deferred producer
                // and never see its artifact (brevo.mail with attach:report after reportingcloud.pdf, say).
                run.Status = StepRunStatus.Pending;
                deferredLeft = true;
                break;
            }

            run.Attempts++;
            try
            {
                var result = await step.ExecuteAsync(ctx, def.Config, ct);
                run.Status = result.Status;
                run.Error = result.Error;
                run.FinishedAt = time.GetUtcNow();
                if (result.Status == StepRunStatus.Failed && critical) broken = true;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogWarning(ex, "Schritt {Step} für {Submission} fehlgeschlagen", def.Step, submission.Id);
                run.Status = StepRunStatus.Failed;
                run.Error = Trim(ex.Message);
                run.FinishedAt = time.GetUtcNow();
                if (critical) broken = true;
            }
        }
        return deferredLeft;
    }

    /// <summary>
    /// Walks the whole pipeline for a test (#21) without any side effect: mail steps send to
    /// <paramref name="adminMailTo"/>, steps with an external write are suppressed and describe what they
    /// would have done, and every step - including those after the confirmation gate - gets a verdict.
    /// Returns one <see cref="TestStepOutcome"/> per step; the submission is never persisted by this method.
    /// </summary>
    public async Task<IReadOnlyList<Domain.UseCases.TestStepOutcome>> RunTestAsync(
        Submission submission, FormDefinition form, int version, string? adminMailTo, CancellationToken ct = default)
    {
        // One synchronous pass over the whole pipeline (#21): the inline/deferred split and the
        // OnConfirm-Waiting gate are deliberately ignored, so every step - including those after
        // doi.request - gets a verdict (AC3/AC8). Nothing here is persisted; the aggregate is discarded.
        var ctx = new StepContext { Submission = submission, Form = form, FormVersion = version, Options = options.Value, Test = new TestRun(adminMailTo) };
        var outcomes = new List<Domain.UseCases.TestStepOutcome>();

        foreach (var def in form.Pipeline)
        {
            var step = Resolve(def.Step);
            IReadOnlyList<Domain.UseCases.TestNote> notes = Array.Empty<Domain.UseCases.TestNote>();
            StepRunStatus status;
            string? error = null;

            if (!ConditionMet(def.When, submission))
            {
                status = StepRunStatus.Skipped;                                     // same condition as a real run
            }
            else if (step.TestBehavior == StepTestBehavior.Redirected)
            {
                if (string.IsNullOrWhiteSpace(adminMailTo))
                {
                    // The issue's rule: report it, never send into the void. Operator-facing text like a
                    // publish-check finding, so German literal rather than a rendered ErrorMessages key.
                    status = StepRunStatus.Failed;
                    error = "Keine Adresse des angemeldeten Admins – der Testversand wurde nicht ausgeführt.";
                }
                else
                {
                    try
                    {
                        var result = await step.ExecuteAsync(ctx, def.Config, ct);  // mail goes to the admin (ctx.Test.MailTo)
                        status = result.Status;
                        error = result.Error;
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        log.LogInformation(ex, "Testschritt {Step} für {Submission} fehlgeschlagen", def.Step, submission.Id);
                        status = StepRunStatus.Failed;
                        error = Trim(ex.Message);
                    }
                }
            }
            else
            {
                status = StepRunStatus.Skipped;                                     // suppressed: no external write (AC5)
                notes = step.DescribeTest(ctx, def.Config);                          // what it would have done (AC6)
            }

            outcomes.Add(new Domain.UseCases.TestStepOutcome(def.Id, def.Step, status, error, notes));
        }
        return outcomes;
    }

    private static bool ConditionMet(string when, Submission s) => when switch
    {
        StepConditions.Always or "" or null => true,
        StepConditions.HasEmail => s.HasEmail,
        var w when w.StartsWith(StepConditions.ResultPrefix, StringComparison.Ordinal) => s.Quiz?.ResultId == w[StepConditions.ResultPrefix.Length..],
        _ => false
    };

    private static string Trim(string s) => s.Length <= 500 ? s : s[..500];
}
