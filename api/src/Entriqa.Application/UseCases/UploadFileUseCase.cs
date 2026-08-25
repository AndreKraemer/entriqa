using Microsoft.Extensions.Options;
using Entriqa.Application.Ports;
using Entriqa.Application.Security;
using Entriqa.Domain.Errors;
using Entriqa.Domain.Forms;
using Entriqa.Domain.UseCases;
using Entriqa.Domain.Validation;

namespace Entriqa.Application.UseCases;

/// <summary>
/// Visitor upload BEFORE submitting: checks the form (published, has a file field), the anti-spam token
/// (signature and age only - the nonce is consumed by the submit), the whitelist and the size, and stores
/// the file privately under uploads/{date}/{guid}/. On submit the submission takes the file over into
/// attachments/; orphaned uploads are cleared by housekeeping after two days.
/// </summary>
internal sealed class UploadFileUseCase(
    ITryGetPublishedFormQuery getPublished,
    IStoreArtifactPort artifacts,
    FormTokenService tokens,
    IOptions<EntriqaOptions> options,
    TimeProvider time) : IUploadFileUseCase
{
    public async Task<UploadedFile> ExecuteAsync(string slug, string token, string fileName, byte[] content, CancellationToken ct = default)
    {
        var published = await getPublished.ExecuteAsync(slug, ct)
            ?? throw new NotFoundException(ErrorCodes.FormNotFound, $"Formular '{slug}' ist nicht veröffentlicht.");
        if (!published.Definition.Fields.Any(f => f.Type == FieldTypes.File))
            throw new AppException(ErrorCodes.Validation, "Dieses Formular nimmt keine Dateien an.");

        tokens.Validate(token, FormTokenService.KindForm, slug,
            TimeSpan.Zero, TimeSpan.FromHours(options.Value.MaxSubmitHours));

        var locale = published.Definition.DefaultLocale;
        if (content.LongLength is 0 or > UploadRules.MaxBytes)
            throw new AppException(ErrorCodes.Validation,
                ValidationMessages.Get(locale, ValidationMessages.UploadTooBig).Replace("{max}", (UploadRules.MaxBytes / 1024 / 1024).ToString()));
        if (!UploadRules.IsAllowed(fileName))
            throw new AppException(ErrorCodes.Validation, ValidationMessages.Get(locale, ValidationMessages.UploadType));

        var safe = UploadRules.SafeName(fileName);
        var path = $"uploads/{time.GetUtcNow():yyyyMMdd}/{Guid.NewGuid():N}/{safe}";
        await artifacts.StoreAsync(path, content, ContentTypeFor(safe), ct);
        return new UploadedFile(path, safe, content.LongLength);
    }

    private static string ContentTypeFor(string name) => Path.GetExtension(name).ToLowerInvariant() switch
    {
        ".pdf" => "application/pdf",
        ".doc" => "application/msword",
        ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        ".xls" => "application/vnd.ms-excel",
        ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        ".ppt" => "application/vnd.ms-powerpoint",
        ".pptx" => "application/vnd.openxmlformats-officedocument.presentationml.presentation",
        ".txt" => "text/plain",
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".gif" => "image/gif",
        ".webp" => "image/webp",
        _ => "application/octet-stream",
    };
}
