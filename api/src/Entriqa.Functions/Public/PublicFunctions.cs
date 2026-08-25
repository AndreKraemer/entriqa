using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Entriqa.Domain.Errors;
using Entriqa.Domain.Submissions;
using Entriqa.Domain.UseCases;
using Entriqa.Functions.Http;

namespace Entriqa.Functions.Public;

/// <summary>The public API that forms.js uses. Functions are thin adapters - one line of use case per endpoint.</summary>
public sealed class PublicFunctions(
    IGetPublishedFormUseCase getForm,
    IIssueFormTokenUseCase issueToken,
    ISubmitFormUseCase submit,
    IRunPendingStepsUseCase run,
    IConfirmSubmissionUseCase confirm,
    IUploadFileUseCase upload,
    ICountFormEventUseCase countEvent)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Function("GetForm")]
    public async Task<IActionResult> GetForm([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "forms/{slug}")] HttpRequest req, string slug, CancellationToken ct)
    {
        var view = await getForm.ExecuteAsync(slug, req.Query["lang"].FirstOrDefault(), ct);
        var etag = $"\"v{view.Version}-{view.Lang}\"";
        req.HttpContext.Response.Headers.CacheControl = "public, max-age=300";
        req.HttpContext.Response.Headers.Vary = "Accept-Language";
        req.HttpContext.Response.Headers.ETag = etag;
        if (req.Headers.IfNoneMatch.Any(v => v == etag || v == "*")) return new StatusCodeResult(StatusCodes.Status304NotModified);
        return new OkObjectResult(view);
    }

    [Function("GetFormToken")]
    public async Task<IActionResult> GetToken([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "forms/{slug}/token")] HttpRequest req, string slug, CancellationToken ct)
    {
        req.HttpContext.Response.Headers.CacheControl = "no-store";
        return new OkObjectResult(new { token = await issueToken.ExecuteAsync(slug, ct) });
    }

    [Function("SubmitForm")]
    public async Task<IActionResult> Submit([HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "forms/{slug}/submissions")] HttpRequest req, string slug, CancellationToken ct)
    {
        var body = await JsonSerializer.DeserializeAsync<SubmitBody>(req.Body, Json, ct)
            ?? throw new AppException(ErrorCodes.Validation, ErrorMessages.EmptyRequest);
        // The language of the submission also settles the error title, otherwise the field errors would speak
        // the form's language and the title the browser's. The middleware cannot read the body itself - it is gone by then.
        RequestLocale.Remember(req.HttpContext, body.Lang);
        var request = new SubmitFormRequest(slug, body.Token ?? "", body.Values ?? new(), body.Answers, body.Website, SwaPrincipalReader.ClientIp(req), body.Lang);
        return new OkObjectResult(await submit.ExecuteAsync(request, ct));
    }

    // File upload by a visitor (multipart, fields "token" and "file"). The response is the handle
    // that forms.js sends along as the field value; the file is only taken over on submit.
    [Function("UploadFile")]
    public async Task<IActionResult> Upload([HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "forms/{slug}/uploads")] HttpRequest req, string slug, CancellationToken ct)
    {
        if (!req.HasFormContentType || req.Form.Files.Count == 0)
            throw new AppException(ErrorCodes.Validation, ErrorMessages.NoFileSubmitted);
        var file = req.Form.Files[0];
        if (file.Length > Domain.Forms.UploadRules.MaxBytes)
            throw new AppException(ErrorCodes.Validation, ErrorMessages.FileTooBig);
        using var ms = new MemoryStream();
        await file.CopyToAsync(ms, ct);
        var result = await upload.ExecuteAsync(slug, req.Form["token"].FirstOrDefault() ?? "", file.FileName, ms.ToArray(), ct);
        return new OkObjectResult(new { upload = result.Path, name = result.Name, size = result.Size });
    }

    // Funnel counters (view | start) - deliberately without personal data; sendBeacon friendly (204, no body needed).
    [Function("CountFormEvent")]
    public async Task<IActionResult> CountEvent([HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "forms/{slug}/events")] HttpRequest req, string slug, CancellationToken ct)
    {
        var body = await JsonSerializer.DeserializeAsync<EventBody>(req.Body, Json, ct) ?? new EventBody(null);
        await countEvent.ExecuteAsync(slug, body.Type ?? "", ct);
        return new NoContentResult();
    }

    [Function("RunPendingSteps")]
    public async Task<IActionResult> Run([HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "submissions/{id}/run")] HttpRequest req, string id, CancellationToken ct)
    {
        var body = await JsonSerializer.DeserializeAsync<RunBody>(req.Body, Json, ct) ?? new RunBody(null);
        await run.ExecuteAsync(id, body.Token ?? "", ct);
        return new AcceptedResult();
    }

    // POST, never GET: link scanners follow GET links from mails and would confirm the opt-in.
    // The GET on the mail link lands on the static /bestaetigen/ page; its button calls this one.
    [Function("ConfirmSubmission")]
    public async Task<IActionResult> Confirm([HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "confirm")] HttpRequest req, CancellationToken ct)
    {
        var body = await JsonSerializer.DeserializeAsync<ConfirmBody>(req.Body, Json, ct) ?? new ConfirmBody(null);
        var result = await confirm.ExecuteAsync(body.Token ?? "", SwaPrincipalReader.ClientIp(req), ct);
        return new OkObjectResult(new { redirect = result.RedirectUrl + (result.AlreadyConfirmed ? "?already=1" : ""), already = result.AlreadyConfirmed });
    }

    private sealed record SubmitBody(string? Token, Dictionary<string, string>? Values, Dictionary<string, string>? Answers, string? Website, string? Lang);
    private sealed record RunBody(string? Token);
    private sealed record EventBody(string? Type);
    private sealed record ConfirmBody(string? Token);
}
