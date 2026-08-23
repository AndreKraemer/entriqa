using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Entriqa.Application.Pipeline;
using Entriqa.Application.Ports;
using Entriqa.Domain.Errors;
using Entriqa.Domain.Submissions;
using Entriqa.Domain.UseCases;

namespace Entriqa.Application.UseCases;

/// <summary>
/// Der garantierte Fallback des Systems (per DevOps-Schedule alle 15 min):
/// 1. Sweep – liegengebliebene Deferred-Läufe nachziehen (Browser weg, sendBeacon verloren).
/// 2. Auto-Retry – fehlgeschlagene Schritte erneut versuchen, bis AutoRetryMax; danach gehört es dem Admin.
/// 3. Retention – Einsendungen nach RetentionDays löschen (samt PDF-Blobs), unbestätigte DOI nach UnconfirmedRetentionDays.
/// 4. Security-Tabellen – abgelaufene Nonces und alte Rate-Limit-Fenster entsorgen.
/// Alles idempotent und Konflikt-tolerant: ein paralleler Confirm-/Run-Lauf gewinnt einfach.
/// </summary>
internal sealed class RunHousekeepingUseCase(
    IListHousekeepingSubmissionsQuery list,
    ITryGetFormVersionQuery getVersion,
    ISaveSubmissionCommand save,
    IDeleteSubmissionCommand delete,
    IPurgeSecurityEntriesCommand purge,
    IRecordHousekeepingRunCommand recordRun,
    IStoreArtifactPort artifacts,
    IListArtifactsPort listArtifacts,
    SubmissionPipelineService pipeline,
    IOptions<EntriqaOptions> options,
    TimeProvider time,
    ILogger<RunHousekeepingUseCase> log) : IRunHousekeepingUseCase
{
    public async Task<HousekeepingResult> ExecuteAsync(CancellationToken ct = default)
    {
        var o = options.Value;
        var now = time.GetUtcNow();
        int swept = 0, retried = 0, deleted = 0;

        // 1 + 2: Sweep und Auto-Retry
        foreach (var s in await list.ListUnfinishedAsync(now.AddMinutes(-o.SweepAfterMinutes), 200, ct))
        {
            var failed = s.StepRuns.Where(r => r.Status == StepRunStatus.Failed).ToList();
            if (failed.Any(r => r.Attempts >= o.AutoRetryMax)) continue;    // ausgereizt – bleibt für den Admin sichtbar liegen

            var v = await getVersion.ExecuteAsync(s.Slug, s.Version, ct);
            if (v is null) { log.LogWarning("Housekeeping: Version {Version} zu {Id} fehlt", s.Version, s.Id); continue; }

            var mode = failed.Count > 0 ? RunMode.Retry : RunMode.Deferred;
            await pipeline.RunAsync(s, v.Definition.Localize(s.Locale), v.Version, mode, null, ct);
            try
            {
                await save.ExecuteAsync(s, ct);
                if (mode == RunMode.Retry) retried++; else swept++;
            }
            catch (AppException ex) when (ex.ErrorCode == ErrorCodes.Conflict) { /* paralleler Lauf war schneller */ }
        }

        // 3: Retention – erst Blobs, dann der Tabelleneintrag (umgekehrt hinterließe ein Crash verwaiste Blobs ohne Zeiger).
        foreach (var s in await list.ListExpiredAsync(now.AddDays(-o.RetentionDays), now.AddDays(-o.UnconfirmedRetentionDays), 500, ct))
        {
            foreach (var path in s.Artifacts.Values.Where(v => !v.Contains("://", StringComparison.Ordinal)))
                await artifacts.DeleteAsync(path, ct);                      // nur Blob-Pfade; "download" ist eine URL
            await delete.ExecuteAsync(s, ct);
            deleted++;
        }

        // 3b: Verwaiste Besucher-Uploads – hochgeladen, aber nie abgeschickt. Übernommene Dateien liegen
        //     längst unter attachments/ und hängen am Lebenszyklus ihrer Einsendung.
        foreach (var upload in await listArtifacts.ListAsync("uploads/", ct))
        {
            var parts = upload.Path.Split('/');
            if (parts.Length < 2 || !DateOnly.TryParseExact(parts[1], "yyyyMMdd", out var day)) continue;
            if (day < DateOnly.FromDateTime(now.UtcDateTime).AddDays(-2))
                await artifacts.DeleteAsync(upload.Path, ct);
        }

        // 4: Security-Tabellen
        var (nonces, rateLimits) = await purge.ExecuteAsync(now, ct);

        var result = new HousekeepingResult(swept, retried, deleted, nonces, rateLimits);
        await recordRun.ExecuteAsync(now,
            $"{result.Swept} nachgezogen, {result.Retried} wiederholt, {result.Deleted} gelöscht, {result.NoncesPurged + result.RateLimitsPurged} Sicherheits-Einträge entsorgt", ct);
        log.LogInformation("Housekeeping: {Swept} nachgezogen, {Retried} wiederholt, {Deleted} gelöscht, {Nonces} Nonces, {RateLimits} Rate-Limit-Fenster",
            result.Swept, result.Retried, result.Deleted, result.NoncesPurged, result.RateLimitsPurged);
        return result;
    }
}
