using Entriqa.Application.Ports;
using Entriqa.Domain.Errors;
using Entriqa.Domain.Forms;
using Entriqa.Domain.UseCases;

namespace Entriqa.Application.UseCases;

internal sealed class GetPublishedFormUseCase(ITryGetPublishedFormQuery getPublished) : IGetPublishedFormUseCase
{
    public async Task<PublicFormView> ExecuteAsync(string slug, string? lang = null, CancellationToken ct = default)
    {
        var v = await getPublished.ExecuteAsync(slug, ct)
            ?? throw new NotFoundException(ErrorCodes.FormNotFound, ErrorMessages.FormNotPublished, AppException.Args("slug", slug));
        var locale = v.Definition.MatchLocale(lang);
        return PublicFormView.From(v.Definition.Localize(locale), v.Version, locale);
    }
}
