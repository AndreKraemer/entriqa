using Entriqa.Application.Ports;
using Entriqa.Application.Security;
using Entriqa.Domain.Errors;
using Entriqa.Domain.UseCases;

namespace Entriqa.Application.UseCases;

internal sealed class IssueFormTokenUseCase(ITryGetPublishedFormQuery getPublished, FormTokenService tokens) : IIssueFormTokenUseCase
{
    public async Task<string> ExecuteAsync(string slug, CancellationToken ct = default)
    {
        _ = await getPublished.ExecuteAsync(slug, ct)
            ?? throw new NotFoundException(ErrorCodes.FormNotFound, $"Formular '{slug}' ist nicht veröffentlicht.");
        return tokens.Issue(FormTokenService.KindForm, slug);
    }
}
