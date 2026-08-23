using Microsoft.Extensions.Options;
using Entriqa.Application.Pipeline;
using Entriqa.Application.Ports;
using Entriqa.Application.Security;
using Entriqa.Domain.Errors;
using Entriqa.Domain.UseCases;

namespace Entriqa.Application.UseCases;

/// <summary>
/// DOI-Bestätigung (POST von der /bestaetigen/-Seite – nie per GET, sonst bestätigen Link-Scanner).
/// Idempotent: ein zweiter Klick startet nichts erneut, holt aber liegengebliebene Waiting-Schritte nach
/// (Crash-Fenster zwischen Bestätigen und Phase 2). Parallel-Klicks entscheidet das ETag.
/// </summary>
internal sealed class ConfirmSubmissionUseCase(
    ITryGetSubmissionQuery getSubmission,
    ITryGetFormVersionQuery getVersion,
    ISaveSubmissionCommand save,
    FormTokenService tokens,
    IpHasher ipHasher,
    SubmissionPipelineService pipeline,
    IOptions<EntriqaOptions> options,
    TimeProvider time) : IConfirmSubmissionUseCase
{
    public async Task<ConfirmResult> ExecuteAsync(string confirmToken, string? clientIp, CancellationToken ct = default)
    {
        // Subject steckt im Token; erst entpacken, dann gegen dieses Subject validieren.
        var subject = PeekSubject(confirmToken);
        tokens.Validate(confirmToken, FormTokenService.KindConfirm, subject, TimeSpan.Zero, TimeSpan.FromDays(options.Value.ConfirmTokenDays));

        var s = await getSubmission.ExecuteAsync(subject, ct)
            ?? throw new NotFoundException(ErrorCodes.SubmissionNotFound, "Einsendung nicht gefunden.");
        var redirect = options.Value.BaseUrl.TrimEnd('/') + options.Value.ConfirmedRedirectPath;
        var already = s.IsConfirmed;

        if (!already)
        {
            s.ConfirmedAt = time.GetUtcNow();
            s.ConfirmedIpHash = ipHasher.Hash(clientIp);
            try { await save.ExecuteAsync(s, ct); }                         // Bestätigung zuerst festhalten, dann Phase 2
            catch (AppException ex) when (ex.ErrorCode == ErrorCodes.Conflict)
            {
                var fresh = await getSubmission.ExecuteAsync(subject, ct);  // Parallel-Klick: der Gewinner führt Phase 2 aus
                if (fresh is null || !fresh.IsConfirmed) throw;
                return new ConfirmResult(fresh.Slug, redirect, AlreadyConfirmed: true);
            }
        }

        if (s.StepRuns.Any(r => r.Status is Domain.Submissions.StepRunStatus.Pending or Domain.Submissions.StepRunStatus.Waiting))
        {
            var v = await getVersion.ExecuteAsync(s.Slug, s.Version, ct)
                ?? throw new NotFoundException(ErrorCodes.FormNotFound, "Formularversion nicht gefunden.");
            await pipeline.RunAsync(s, v.Definition.Localize(s.Locale), v.Version, RunMode.Deferred, null, ct);
            try { await save.ExecuteAsync(s, ct); }
            catch (AppException ex) when (ex.ErrorCode == ErrorCodes.Conflict) { /* paralleler Lauf schreibt bereits */ }
        }
        return new ConfirmResult(s.Slug, redirect, already);
    }

    private static string PeekSubject(string token)
    {
        try
        {
            var p = token[..token.IndexOf('.')].Replace('-', '+').Replace('_', '/');
            var payload = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(p.PadRight(p.Length + (4 - p.Length % 4) % 4, '=')));
            return payload.Split('|')[1];
        }
        catch (Exception) { throw new SecurityTokenException(ErrorCodes.TokenInvalid, "Link ungültig."); }
    }
}
