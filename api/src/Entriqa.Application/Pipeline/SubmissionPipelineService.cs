using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Entriqa.Domain.Errors;
using Entriqa.Domain.Forms;
using Entriqa.Domain.Submissions;

namespace Entriqa.Application.Pipeline;

public enum RunMode
{
    Inline,     // im Submit-Request: nur Inline-Schritte, Deferred bleiben Pending
    Deferred,   // Hintergrundlauf bzw. nach Bestätigung: alles, was ausführbar ist
    Retry       // wie Deferred, setzt vorher Failed/Blocked zurück
}

/// <summary>
/// Der Runner: streng sequenziell, ein Fehler blockiert die Folgeschritte, jeder Schritt hat seinen eigenen Status.
/// Kein Use Case (keine Benutzeroperation), sondern Domain Service nach Solution Standard §13.5.
/// </summary>
public sealed class SubmissionPipelineService(
    IEnumerable<ISubmissionStep> steps,
    IOptions<EntriqaOptions> options,
    TimeProvider time,
    ILogger<SubmissionPipelineService> log)
{
    private readonly Dictionary<string, ISubmissionStep> _steps = steps.ToDictionary(s => s.Key);

    public ISubmissionStep Resolve(string key) =>
        _steps.TryGetValue(key, out var s) ? s : throw new AppException(ErrorCodes.StepUnknown, $"Unbekannter Schritt '{key}'.", 500);

    /// <summary>Für Prüfregeln: unbekannte Schlüssel sind dort ein Issue, kein Serverfehler.</summary>
    public ISubmissionStep? TryResolve(string key) => _steps.GetValueOrDefault(key);

    /// <summary>Legt die Schrittläufe beim Anlegen der Einsendung an (Phase aus der Position relativ zum teilenden Schritt).</summary>
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

    /// <summary>Gibt true zurück, wenn danach noch Deferred-Schritte ausstehen (→ Run-Token an den Client).</summary>
    public async Task<bool> RunAsync(Submission submission, FormDefinition form, int version, RunMode mode, string? onlyStepId = null, CancellationToken ct = default)
    {
        if (mode == RunMode.Retry)
            foreach (var r in submission.StepRuns.Where(r => r.Status is StepRunStatus.Failed or StepRunStatus.Blocked && (onlyStepId is null || r.StepId == onlyStepId || r.Status == StepRunStatus.Blocked)))
            { r.Status = StepRunStatus.Pending; r.Error = null; }

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
                // Ein Deferred-Schritt teilt den Lauf (wie doi.request die Phasen teilt): alles ab hier läuft
                // erst im Deferred-Lauf. Sonst würde ein Inline-Konsument vor seinem Deferred-Produzenten laufen
                // und dessen Artefakt nie sehen (z. B. brevo.mail mit attach:report nach reportingcloud.pdf).
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

    private static bool ConditionMet(string when, Submission s) => when switch
    {
        StepConditions.Always or "" or null => true,
        StepConditions.HasEmail => s.HasEmail,
        var w when w.StartsWith(StepConditions.ResultPrefix, StringComparison.Ordinal) => s.Quiz?.ResultId == w[StepConditions.ResultPrefix.Length..],
        _ => false
    };

    private static string Trim(string s) => s.Length <= 500 ? s : s[..500];
}
