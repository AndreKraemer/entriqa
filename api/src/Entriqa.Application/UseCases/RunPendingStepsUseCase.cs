using Entriqa.Application.Pipeline;
using Entriqa.Application.Ports;
using Entriqa.Application.Security;
using Entriqa.Domain.Errors;
using Entriqa.Domain.UseCases;

namespace Entriqa.Application.UseCases;

internal sealed class RunPendingStepsUseCase(
    ITryGetSubmissionQuery getSubmission,
    ITryGetFormVersionQuery getVersion,
    ISaveSubmissionCommand save,
    ITryConsumeNonceCommand consumeNonce,
    FormTokenService tokens,
    SubmissionPipelineService pipeline) : IRunPendingStepsUseCase
{
    public async Task ExecuteAsync(string submissionId, string runToken, CancellationToken ct = default)
    {
        var payload = tokens.Validate(runToken, FormTokenService.KindRun, submissionId, TimeSpan.Zero, TimeSpan.FromHours(1));

        // Run-Token sind Einweg: forms.js feuert vor Redirects zusätzlich per sendBeacon – Doppelankünfte
        // und Replays laufen hier still ins Leere; das Housekeeping ist der garantierte Fallback.
        if (!await consumeNonce.ExecuteAsync(payload.Nonce, payload.IssuedAt.AddHours(1), ct)) return;

        var s = await getSubmission.ExecuteAsync(submissionId, ct)
            ?? throw new NotFoundException(ErrorCodes.SubmissionNotFound, "Einsendung nicht gefunden.");
        var v = await getVersion.ExecuteAsync(s.Slug, s.Version, ct)
            ?? throw new NotFoundException(ErrorCodes.FormNotFound, "Formularversion nicht gefunden.");
        await pipeline.RunAsync(s, v.Definition.Localize(s.Locale), v.Version, RunMode.Deferred, null, ct);
        try { await save.ExecuteAsync(s, ct); }
        catch (AppException ex) when (ex.ErrorCode == ErrorCodes.Conflict) { /* Confirm/Retry war schneller – dessen Lauf deckt die Schritte ab */ }
    }
}
