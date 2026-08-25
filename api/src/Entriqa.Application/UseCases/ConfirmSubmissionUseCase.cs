using Microsoft.Extensions.Options;
using Entriqa.Application.Pipeline;
using Entriqa.Application.Ports;
using Entriqa.Application.Security;
using Entriqa.Domain.Errors;
using Entriqa.Domain.UseCases;

namespace Entriqa.Application.UseCases;

/// <summary>
/// DOI confirmation (a POST from the /bestaetigen/ page - never via GET, or link scanners confirm it).
/// Idempotent: a second click starts nothing again but does pick up waiting steps left behind
/// (the crash window between confirming and phase 2). Parallel clicks are decided by the ETag.
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
        // The subject sits inside the token; unpack first, then validate against that subject.
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
            try { await save.ExecuteAsync(s, ct); }                         // record the confirmation first, then phase 2
            catch (AppException ex) when (ex.ErrorCode == ErrorCodes.Conflict)
            {
                var fresh = await getSubmission.ExecuteAsync(subject, ct);  // parallel click: the winner runs phase 2
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
            catch (AppException ex) when (ex.ErrorCode == ErrorCodes.Conflict) { /* a parallel run is already writing */ }
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
