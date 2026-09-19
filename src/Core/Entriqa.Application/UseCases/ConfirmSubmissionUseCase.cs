using Microsoft.Extensions.Options;
using Entriqa.Application.Consent;
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
    ConsentProofService consentProofs,
    IOptions<EntriqaOptions> options,
    TimeProvider time) : IConfirmSubmissionUseCase
{
    public async Task<ConfirmResult> ExecuteAsync(string confirmToken, string? clientIp, CancellationToken ct = default)
    {
        // The subject sits inside the token; unpack first, then validate against that subject.
        var subject = PeekSubject(confirmToken);

        // A confirm link from a test mail (#21, AC9): it confirms nothing. Verify the token is genuine, then
        // report it as a test - no submission is looked up (there is none), and the page shows a test hint.
        if (PeekKind(confirmToken) == FormTokenService.KindConfirmTest)
        {
            tokens.Validate(confirmToken, FormTokenService.KindConfirmTest, subject, TimeSpan.Zero, TimeSpan.FromDays(options.Value.ConfirmTokenDays));
            var testRedirect = options.Value.BaseUrl.TrimEnd('/') + options.Value.ConfirmedRedirectPathFor(null);
            return new ConfirmResult(subject.Split(':')[0], testRedirect, AlreadyConfirmed: false, IsTest: true);
        }

        tokens.Validate(confirmToken, FormTokenService.KindConfirm, subject, TimeSpan.Zero, TimeSpan.FromDays(options.Value.ConfirmTokenDays));

        var s = await getSubmission.ExecuteAsync(subject, ct)
            ?? throw new NotFoundException(ErrorCodes.SubmissionNotFound, ErrorMessages.SubmissionNotFound);
        var redirect = options.Value.BaseUrl.TrimEnd('/') + options.Value.ConfirmedRedirectPathFor(s.Locale);
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
            await consentProofs.ConfirmAsync(s, ct);                        // #1: amends the proof, never creates one
        }

        if (s.StepRuns.Any(r => r.Status is Domain.Submissions.StepRunStatus.Pending or Domain.Submissions.StepRunStatus.Waiting))
        {
            var v = await getVersion.ExecuteAsync(s.Slug, s.Version, ct)
                ?? throw new NotFoundException(ErrorCodes.FormNotFound, ErrorMessages.FormVersionNotFound);
            await pipeline.RunAsync(s, v.Definition.Localize(s.Locale), v.Version, RunMode.Deferred, null, null, ct);
            try { await save.ExecuteAsync(s, ct); }
            catch (AppException ex) when (ex.ErrorCode == ErrorCodes.Conflict) { /* a parallel run is already writing */ }
        }
        return new ConfirmResult(s.Slug, redirect, already);
    }

    private static string PeekKind(string token) => PeekPart(token, 0);
    private static string PeekSubject(string token) => PeekPart(token, 1);

    private static string PeekPart(string token, int index)
    {
        try
        {
            var p = token[..token.IndexOf('.')].Replace('-', '+').Replace('_', '/');
            var payload = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(p.PadRight(p.Length + (4 - p.Length % 4) % 4, '=')));
            return payload.Split('|')[index];
        }
        catch (Exception) { throw new SecurityTokenException(ErrorCodes.TokenInvalid, ErrorMessages.LinkInvalid); }
    }
}
