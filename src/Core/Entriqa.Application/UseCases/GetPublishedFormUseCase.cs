using Entriqa.Application.Ports;
using Entriqa.Domain.Errors;
using Entriqa.Domain.Forms;
using Entriqa.Domain.UseCases;

namespace Entriqa.Application.UseCases;

internal sealed class GetPublishedFormUseCase(ITryGetPublishedFormQuery getPublished, AppointmentOfferService offer) : IGetPublishedFormUseCase
{
    public async Task<PublicFormView> ExecuteAsync(string slug, string? lang = null, CancellationToken ct = default)
    {
        var v = await getPublished.ExecuteAsync(slug, ct)
            ?? throw new NotFoundException(ErrorCodes.FormNotFound, ErrorMessages.FormNotPublished, AppException.Args("slug", slug));
        var locale = v.Definition.MatchLocale(lang);
        var def = v.Definition.Localize(locale);
        return PublicFormView.From(def, v.Version, locale, await offer.ForAsync(def, ct));
    }
}
