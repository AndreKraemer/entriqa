using Entriqa.Application.Pipeline;
using Entriqa.Application.Ports;
using Entriqa.Domain.Errors;
using Entriqa.Domain.Forms;
using Entriqa.Domain.UseCases;

namespace Entriqa.Application.UseCases;

internal sealed class ListFormsUseCase(IListFormsQuery list) : IListFormsUseCase
{
    public Task<IReadOnlyList<FormListItem>> ExecuteAsync(CancellationToken ct = default) => list.ExecuteAsync(ct);
}

internal sealed class GetFormDraftUseCase(ITryGetFormDraftQuery getDraft) : IGetFormDraftUseCase
{
    public async Task<FormDraftView> ExecuteAsync(string slug, CancellationToken ct = default)
    {
        var d = await getDraft.ExecuteAsync(slug, ct)
            ?? throw new NotFoundException(ErrorCodes.FormNotFound, $"Formular '{slug}' ist unbekannt.");
        return new FormDraftView(d.Slug, d.Status, d.PublishedVersion, d.UpdatedAt, d.UpdatedBy, d.Definition);
    }
}

internal sealed class SaveFormDraftUseCase(ISaveFormDraftCommand save) : ISaveFormDraftUseCase
{
    public Task ExecuteAsync(FormDefinition definition, string savedBy, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(definition.Slug) || definition.Slug.Any(c => !(char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '-')))
            throw new AppException(ErrorCodes.Validation, "Slug darf nur Kleinbuchstaben, Ziffern und Bindestriche enthalten.");
        return save.ExecuteAsync(definition, savedBy, ct);
    }
}

/// <summary>Prüfen und veröffentlichen in einem Zug – Issues sperren, wie im Admin live angezeigt.</summary>
internal sealed class PublishFormUseCase(
    ITryGetFormDraftQuery getDraft,
    IPublishFormVersionCommand publish,
    PublishCheckService check) : IPublishFormUseCase
{
    public async Task<PublishFormResult> ExecuteAsync(string slug, string publishedBy, CancellationToken ct = default)
    {
        var d = await getDraft.ExecuteAsync(slug, ct)
            ?? throw new NotFoundException(ErrorCodes.FormNotFound, $"Formular '{slug}' ist unbekannt.");
        var issues = check.Check(d.Definition);
        if (issues.Count > 0) return new PublishFormResult(0, issues);
        var version = await publish.ExecuteAsync(d.Definition, publishedBy, ct);
        return new PublishFormResult(version.Version, Array.Empty<string>());
    }
}
