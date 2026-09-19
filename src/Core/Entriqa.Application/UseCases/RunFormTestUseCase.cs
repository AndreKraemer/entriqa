using Microsoft.Extensions.Options;
using Entriqa.Application.Pipeline;
using Entriqa.Application.Ports;
using Entriqa.Domain.UseCases;

namespace Entriqa.Application.UseCases;

/// <summary>
/// Test mode (#21): loads the <em>draft</em>, validates and scores it exactly as a real submission would,
/// then walks the whole pipeline without side effects. No anti-spam (honeypot, token, rate limit) - the
/// caller is an authenticated admin, not a bot - and nothing is ever stored: the aggregate lives only for
/// the walk and is discarded, so no test submission reaches the inbox, the reporting or the view count.
/// </summary>
internal sealed class RunFormTestUseCase(
    ITryGetFormDraftQuery getDraft,
    SubmissionPipelineService pipeline,
    IOptions<EntriqaOptions> options,
    TimeProvider time) : IRunFormTestUseCase
{
    public Task<FormTestResult> ExecuteAsync(FormTestRequest request, CancellationToken ct = default)
    {
        _ = (getDraft, pipeline, options, time);    // skeleton (#21): dependencies wired, no logic yet
        throw new NotImplementedException();
    }
}
