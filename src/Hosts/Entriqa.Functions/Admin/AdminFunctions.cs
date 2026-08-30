using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Entriqa.Domain.Forms;
using Entriqa.Domain.UseCases;
using Entriqa.Functions.Http;

namespace Entriqa.Functions.Admin;

/// <summary>Admin API (role "admin", routes under manage/ - /admin is the Blazor UI). Still open: CSV export, statistics, AdminState.</summary>
public sealed class AdminFunctions(
    SwaPrincipalReader principal,
    IListSubmissionsUseCase list,
    IRetryStepUseCase retry,
    IGetStepCatalogUseCase catalog,
    ICheckFormForPublishUseCase check,
    IListFormsUseCase listForms,
    IGetFormDraftUseCase getDraft,
    ISaveFormDraftUseCase saveDraft,
    IPublishFormUseCase publish,
    IGetSubmissionDetailUseCase getSubmission,
    ISetSubmissionHandlingUseCase setHandling,
    IDeleteSubmissionAdminUseCase deleteSubmission,
    IListRecentSubmissionsUseCase recent,
    IMarkVisitedUseCase markVisited,
    IGetFormStatsUseCase stats,
    IGetIntegrationDirectoryUseCase directory,
    IUploadLeadMagnetUseCase upload,
    IExportSubmissionsCsvUseCase exportCsv,
    IResendDoiUseCase resendDoi,
    IGetFormsActivityUseCase activity,
    IGetAdminStatusUseCase status,
    IListContactsUseCase listContacts,
    IListContactSubmissionsUseCase contactSubmissions,
    IDeleteContactUseCase deleteContact,
    Entriqa.Application.Ports.ICreateDownloadLinkPort downloadLinks)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Function("AdminListContacts")]
    public async Task<IActionResult> Contacts([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "manage/contacts")] HttpRequest req, CancellationToken ct)
    {
        principal.RequireRole(req, "admin");
        return new OkObjectResult(await listContacts.ExecuteAsync(ct));
    }

    [Function("AdminContactSubmissions")]
    public async Task<IActionResult> ContactSubmissions([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "manage/contacts/submissions")] HttpRequest req, CancellationToken ct)
    {
        principal.RequireRole(req, "admin");
        var email = req.Query["email"].FirstOrDefault() ?? "";
        return new OkObjectResult(await contactSubmissions.ExecuteAsync(email, ct));
    }

    /// <summary>GDPR deletion: every submission of that address including its blobs.</summary>
    [Function("AdminDeleteContact")]
    public async Task<IActionResult> DeleteContact([HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "manage/contacts")] HttpRequest req, CancellationToken ct)
    {
        principal.RequireRole(req, "admin");
        var email = req.Query["email"].FirstOrDefault() ?? "";
        if (email.Length == 0) return new BadRequestResult();
        return new OkObjectResult(new { deleted = await deleteContact.ExecuteAsync(email, principal.UserName(req), ct) });
    }

    /// <summary>Time-limited download link to a visitor upload (a value of one submission).</summary>
    [Function("AdminUploadLink")]
    public async Task<IActionResult> UploadLink([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "manage/uploads/link")] HttpRequest req, CancellationToken ct)
    {
        principal.RequireRole(req, "admin");
        var path = req.Query["path"].FirstOrDefault() ?? "";
        if (!(path.StartsWith("attachments/", StringComparison.Ordinal) || path.StartsWith("uploads/", StringComparison.Ordinal))
            || path.Contains("..", StringComparison.Ordinal))
            return new BadRequestResult();
        return new OkObjectResult(new { url = (await downloadLinks.CreateAsync(path, TimeSpan.FromMinutes(10), ct)).ToString() });
    }

    [Function("AdminRecentSubmissions")]
    public async Task<IActionResult> Recent([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "manage/submissions")] HttpRequest req, CancellationToken ct)
    {
        principal.RequireRole(req, "admin");
        var slug = req.Query["slug"].FirstOrDefault();
        return new OkObjectResult(await recent.ExecuteAsync(string.IsNullOrEmpty(slug) ? null : slug, principal.UserName(req), 500, ct));
    }

    [Function("AdminMarkVisited")]
    public async Task<IActionResult> Visited([HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "manage/state/last-visit")] HttpRequest req, CancellationToken ct)
    {
        principal.RequireRole(req, "admin");
        return new OkObjectResult(new { lastVisitAt = await markVisited.ExecuteAsync(principal.UserName(req), ct) });
    }

    [Function("AdminFormStats")]
    public async Task<IActionResult> Stats([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "manage/forms/{slug}/stats")] HttpRequest req, string slug, CancellationToken ct)
    {
        principal.RequireRole(req, "admin");
        int? version = int.TryParse(req.Query["version"].FirstOrDefault(), out var v) ? v : null;
        return new OkObjectResult(await stats.ExecuteAsync(slug, version, ct));
    }

    [Function("AdminListForms")]
    public async Task<IActionResult> Forms([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "manage/forms")] HttpRequest req, CancellationToken ct)
    {
        principal.RequireRole(req, "admin");
        return new OkObjectResult(await listForms.ExecuteAsync(ct));
    }

    [Function("AdminGetFormDraft")]
    public async Task<IActionResult> GetDraft([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "manage/forms/{slug}")] HttpRequest req, string slug, CancellationToken ct)
    {
        principal.RequireRole(req, "admin");
        return new OkObjectResult(await getDraft.ExecuteAsync(slug, ct));
    }

    [Function("AdminSaveFormDraft")]
    public async Task<IActionResult> SaveDraft([HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "manage/forms/{slug}")] HttpRequest req, string slug, CancellationToken ct)
    {
        principal.RequireRole(req, "admin");
        var def = await JsonSerializer.DeserializeAsync<FormDefinition>(req.Body, Json, ct);
        if (def is null) return new BadRequestResult();
        if (!string.Equals(def.Slug, slug, StringComparison.Ordinal))
            throw new Entriqa.Domain.Errors.AppException(Entriqa.Domain.Errors.ErrorCodes.Validation, "Slug in URL und Definition stimmen nicht überein.");
        await saveDraft.ExecuteAsync(def, principal.UserName(req), ct);
        return new NoContentResult();
    }

    [Function("AdminPublishForm")]
    public async Task<IActionResult> Publish([HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "manage/forms/{slug}/publish")] HttpRequest req, string slug, CancellationToken ct)
    {
        principal.RequireRole(req, "admin");
        return new OkObjectResult(await publish.ExecuteAsync(slug, principal.UserName(req), ct));
    }

    [Function("AdminGetSubmission")]
    public async Task<IActionResult> GetSubmission([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "manage/submissions/{id}")] HttpRequest req, string id, CancellationToken ct)
    {
        principal.RequireRole(req, "admin");
        return new OkObjectResult(await getSubmission.ExecuteAsync(id, ct));
    }

    [Function("AdminSetHandling")]
    public async Task<IActionResult> SetHandling([HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "manage/submissions/{id}/handling")] HttpRequest req, string id, CancellationToken ct)
    {
        principal.RequireRole(req, "admin");
        var body = await JsonSerializer.DeserializeAsync<HandlingBody>(req.Body, Json, ct) ?? new HandlingBody(null);
        await setHandling.ExecuteAsync(id, body.Handling ?? "", ct);
        return new NoContentResult();
    }

    [Function("AdminDeleteSubmission")]
    public async Task<IActionResult> Delete([HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "manage/submissions/{id}")] HttpRequest req, string id, CancellationToken ct)
    {
        principal.RequireRole(req, "admin");
        await deleteSubmission.ExecuteAsync(id, ct);
        return new NoContentResult();
    }

    [Function("AdminFormsActivity")]
    public async Task<IActionResult> Activity([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "manage/forms/activity")] HttpRequest req, CancellationToken ct)
    {
        principal.RequireRole(req, "admin");
        return new OkObjectResult(await activity.ExecuteAsync(ct));
    }

    [Function("AdminStatus")]
    public async Task<IActionResult> Status([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "manage/status")] HttpRequest req, CancellationToken ct)
    {
        principal.RequireRole(req, "admin");
        return new OkObjectResult(await status.ExecuteAsync(ct));
    }

    [Function("AdminDirectory")]
    public async Task<IActionResult> Directory([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "manage/directory")] HttpRequest req, CancellationToken ct)
    {
        principal.RequireRole(req, "admin");
        return new OkObjectResult(await directory.ExecuteAsync(ct));
    }

    [Function("AdminUploadLeadMagnet")]
    public async Task<IActionResult> Upload([HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "manage/blobs/leadmagnets")] HttpRequest req, CancellationToken ct)
    {
        principal.RequireRole(req, "admin");
        var form = await req.ReadFormAsync(ct);
        var file = form.Files.Count > 0 ? form.Files[0] : null;
        if (file is null) return new BadRequestResult();
        using var ms = new MemoryStream();
        await file.CopyToAsync(ms, ct);
        return new OkObjectResult(await upload.ExecuteAsync(file.FileName, ms.ToArray(), file.ContentType, ct));
    }

    [Function("AdminExportCsv")]
    public async Task<IActionResult> Export([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "manage/forms/{slug}/export.csv")] HttpRequest req, string slug, CancellationToken ct)
    {
        principal.RequireRole(req, "admin");
        var csv = await exportCsv.ExecuteAsync(slug, ct);
        req.HttpContext.Response.Headers.ContentDisposition = $"attachment; filename=\"{slug}-einsendungen.csv\"";
        return new ContentResult { Content = csv, ContentType = "text/csv; charset=utf-8", StatusCode = 200 };
    }

    [Function("AdminResendDoi")]
    public async Task<IActionResult> ResendDoi([HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "manage/submissions/{id}/resend-doi")] HttpRequest req, string id, CancellationToken ct)
    {
        principal.RequireRole(req, "admin");
        await resendDoi.ExecuteAsync(id, ct);
        return new AcceptedResult();
    }

    private sealed record HandlingBody(string? Handling);

    [Function("AdminListSubmissions")]
    public async Task<IActionResult> List([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "manage/forms/{slug}/submissions")] HttpRequest req, string slug, CancellationToken ct)
    {
        principal.RequireRole(req, "admin");
        var page = await list.ExecuteAsync(slug, req.Query["continuation"].ToString() is { Length: > 0 } c ? c : null, 25, ct);
        return new OkObjectResult(page);
    }

    [Function("AdminRetryStep")]
    public async Task<IActionResult> Retry([HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "manage/submissions/{id}/steps/{stepId}/retry")] HttpRequest req, string id, string stepId, CancellationToken ct)
    {
        principal.RequireRole(req, "admin");
        await retry.ExecuteAsync(id, stepId, ct);
        return new AcceptedResult();
    }

    [Function("AdminStepCatalog")]
    public async Task<IActionResult> Steps([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "manage/steps")] HttpRequest req, CancellationToken ct)
    {
        principal.RequireRole(req, "admin");
        return new OkObjectResult(await catalog.ExecuteAsync(ct));
    }

    [Function("AdminCheckForm")]
    public async Task<IActionResult> Check([HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "manage/forms/check")] HttpRequest req, CancellationToken ct)
    {
        principal.RequireRole(req, "admin");
        var def = await JsonSerializer.DeserializeAsync<FormDefinition>(req.Body, Json, ct);
        return def is null ? new BadRequestResult() : new OkObjectResult(new { issues = await check.ExecuteAsync(def, ct) });
    }
}
