using Entriqa.Application.Pipeline;
using Entriqa.Application.Ports;
using Entriqa.Domain.Errors;
using Entriqa.Domain.Forms;
using Entriqa.Domain.Submissions;
using Entriqa.Domain.UseCases;

namespace Entriqa.Application.UseCases;

internal sealed class ListSubmissionsUseCase(IListSubmissionsQuery list) : IListSubmissionsUseCase
{
    public Task<SubmissionPage> ExecuteAsync(string slug, string? continuationToken, int pageSize = 25, CancellationToken ct = default)
        => list.ExecuteAsync(slug, continuationToken, Math.Clamp(pageSize, 1, 100), ct);
}

internal sealed class RetryStepUseCase(
    ITryGetSubmissionQuery getSubmission,
    ITryGetFormVersionQuery getVersion,
    ISaveSubmissionCommand save,
    SubmissionPipelineService pipeline) : IRetryStepUseCase
{
    public async Task ExecuteAsync(string submissionId, string stepId, string? by, CancellationToken ct = default)
    {
        var s = await getSubmission.ExecuteAsync(submissionId, ct)
            ?? throw new NotFoundException(ErrorCodes.SubmissionNotFound, ErrorMessages.SubmissionNotFound);
        var v = await getVersion.ExecuteAsync(s.Slug, s.Version, ct)
            ?? throw new NotFoundException(ErrorCodes.FormNotFound, ErrorMessages.FormVersionNotFound);
        await pipeline.RunAsync(s, v.Definition.Localize(s.Locale), v.Version, RunMode.Retry, stepId, null, ct);
        await save.ExecuteAsync(s, ct);
    }
}

internal sealed class GetStepCatalogUseCase(StepCatalogService catalog) : IGetStepCatalogUseCase
{
    public Task<IReadOnlyList<StepDescriptor>> ExecuteAsync(CancellationToken ct = default) => Task.FromResult(catalog.Describe());
}

internal sealed class CheckFormForPublishUseCase(PublishCheckService check) : ICheckFormForPublishUseCase
{
    public Task<IReadOnlyList<string>> ExecuteAsync(FormDefinition definition, CancellationToken ct = default) => Task.FromResult(check.Check(definition));
}
