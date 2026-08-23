using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Entriqa.Application.Pipeline;
using Entriqa.Application.Ports;
using Entriqa.Domain.Errors;
using Entriqa.Domain.Forms;
using Entriqa.Domain.UseCases;

namespace Entriqa.Application.UseCases;

internal sealed class GetIntegrationDirectoryUseCase(
    IBrevoDirectoryPort brevo,
    IListReportTemplatesPort reportTemplates,
    IListArtifactsPort artifacts,
    IOptions<EntriqaOptions> options,
    ILogger<GetIntegrationDirectoryUseCase> log) : IGetIntegrationDirectoryUseCase
{
    public async Task<IntegrationDirectory> ExecuteAsync(CancellationToken ct = default)
    {
        var o = options.Value;
        var lists = new List<DirectoryEntry>();
        var templates = new List<DirectoryEntry>();
        var rc = new List<string>();

        if (!string.IsNullOrEmpty(o.Brevo.ApiKey))
        {
            try
            {
                lists.AddRange((await brevo.GetListsAsync(ct)).Select(l => new DirectoryEntry(l.Id, l.Name)));
                templates.AddRange((await brevo.GetTemplatesAsync(ct)).Select(t => new DirectoryEntry(t.Id, t.Name)));
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { log.LogWarning(ex, "Brevo-Verzeichnis nicht erreichbar"); }
        }
        if (!string.IsNullOrEmpty(o.ReportingCloud.ApiKey))
        {
            try { rc.AddRange(await reportTemplates.ExecuteAsync(ct)); }
            catch (Exception ex) when (ex is not OperationCanceledException) { log.LogWarning(ex, "ReportingCloud-Vorlagen nicht erreichbar"); }
        }
        var magnets = (await artifacts.ListAsync("leadmagnets/", ct)).Select(a => new LeadMagnetInfo(a.Path, a.Size)).ToList();

        return new IntegrationDirectory(
            !string.IsNullOrEmpty(o.Brevo.ApiKey), lists, templates,
            !string.IsNullOrEmpty(o.ReportingCloud.ApiKey), rc, magnets);
    }
}

internal sealed class UploadLeadMagnetUseCase(IStoreArtifactPort store) : IUploadLeadMagnetUseCase
{
    public async Task<LeadMagnetInfo> ExecuteAsync(string fileName, byte[] content, string contentType, CancellationToken ct = default)
    {
        if (content.Length == 0) throw new AppException(ErrorCodes.Validation, "Leere Datei.");
        if (content.Length > 25 * 1024 * 1024) throw new AppException(ErrorCodes.Validation, "Datei größer als 25 MB.");
        var safe = string.Concat(Path.GetFileName(fileName).Select(c => char.IsLetterOrDigit(c) || c is '.' or '-' or '_' ? c : '-'));
        if (safe.Length == 0) throw new AppException(ErrorCodes.Validation, "Dateiname fehlt.");
        var path = await store.StoreAsync($"leadmagnets/{safe}", content,
            string.IsNullOrEmpty(contentType) ? "application/octet-stream" : contentType, ct);
        return new LeadMagnetInfo(path, content.Length);
    }
}

internal sealed class ExportSubmissionsCsvUseCase(
    IListSubmissionsForStatsQuery list,
    ITryGetPublishedFormQuery getPublished) : IExportSubmissionsCsvUseCase
{
    public async Task<string> ExecuteAsync(string slug, CancellationToken ct = default)
    {
        var published = await getPublished.ExecuteAsync(slug, ct)
            ?? throw new NotFoundException(ErrorCodes.FormNotFound, $"Formular '{slug}' ist nicht veröffentlicht.");
        var def = published.Definition.Localize(null);
        var fields = def.Fields.Where(f => !FieldTypes.IsLayout(f.Type)).ToList();
        var items = (await list.ExecuteAsync(slug, 5000, ct)).OrderByDescending(s => s.CreatedAt).ToList();

        var sb = new StringBuilder();
        var head = new List<string> { "Id", "Eingang", "Sprache", "Status", "Bearbeitung", "DOI bestätigt", "Quelle" };
        head.AddRange(fields.Select(f => f.Label.ToString()));
        if (def.Quiz is not null) { head.Add("Quiz-Ergebnis"); head.Add("Prozent"); }
        sb.AppendLine(string.Join(';', head.Select(Csv)));

        foreach (var s in items)
        {
            var row = new List<string?>
            {
                s.Id, s.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss"), s.Locale, s.State.ToString(), s.Handling,
                s.ConfirmedAt?.ToString("yyyy-MM-dd HH:mm:ss"), s.Source,
            };
            row.AddRange(fields.Select(f => s.Values.GetValueOrDefault(f.Id)));
            if (def.Quiz is not null) { row.Add(s.Quiz?.ResultId); row.Add(s.Quiz?.Pct.ToString()); }
            sb.AppendLine(string.Join(';', row.Select(Csv)));
        }
        return sb.ToString();
    }

    private static string Csv(string? v)
    {
        if (string.IsNullOrEmpty(v)) return "";
        var needsQuotes = v.Contains(';') || v.Contains('"') || v.Contains('\n') || v.Contains('\r');
        return needsQuotes ? "\"" + v.Replace("\"", "\"\"") + "\"" : v;
    }
}

internal sealed class ResendDoiUseCase(
    ITryGetSubmissionQuery getSubmission,
    ITryGetFormVersionQuery getVersion,
    SubmissionPipelineService pipeline,
    IOptions<EntriqaOptions> options) : IResendDoiUseCase
{
    public async Task ExecuteAsync(string submissionId, CancellationToken ct = default)
    {
        var s = await getSubmission.ExecuteAsync(submissionId, ct)
            ?? throw new NotFoundException(ErrorCodes.SubmissionNotFound, "Einsendung nicht gefunden.");
        if (s.IsConfirmed) throw new AppException(ErrorCodes.Validation, "Schon bestätigt – nichts zu senden.");
        var v = await getVersion.ExecuteAsync(s.Slug, s.Version, ct)
            ?? throw new NotFoundException(ErrorCodes.FormNotFound, "Formularversion nicht gefunden.");
        var def = v.Definition.Localize(s.Locale);
        var doi = def.Pipeline.FirstOrDefault(st => st.Step == "doi.request")
            ?? throw new AppException(ErrorCodes.Validation, "Dieses Formular hat kein Double-Opt-In.");

        var ctx = new StepContext { Submission = s, Form = def, FormVersion = v.Version, Options = options.Value };
        await pipeline.Resolve(doi.Step).ExecuteAsync(ctx, doi.Config, ct);   // idempotent: nach Bestätigung tut der Schritt nichts
    }
}
