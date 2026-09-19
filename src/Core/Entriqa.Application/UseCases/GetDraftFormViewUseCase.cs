using Entriqa.Application.Ports;
using Entriqa.Domain.Errors;
using Entriqa.Domain.Forms;
using Entriqa.Domain.UseCases;

namespace Entriqa.Application.UseCases;

/// <summary>
/// The draft as the resolved, single-language view forms.js renders (#21, AC1). Same shape as
/// <see cref="GetPublishedFormUseCase"/>, but from the draft - so a test form looks like the visitor's.
/// </summary>
internal sealed class GetDraftFormViewUseCase(ITryGetFormDraftQuery getDraft) : IGetDraftFormViewUseCase
{
    public async Task<PublicFormView> ExecuteAsync(string slug, string? lang = null, CancellationToken ct = default)
    {
        var draft = await getDraft.ExecuteAsync(slug, ct)
            ?? throw new NotFoundException(ErrorCodes.FormNotFound, ErrorMessages.FormUnknown, AppException.Args("slug", slug));
        var locale = draft.Definition.MatchLocale(lang);
        return PublicFormView.From(draft.Definition.Localize(locale), draft.PublishedVersion, locale);
    }
}
