using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Entriqa.Application.Pipeline;
using Entriqa.Application.Ports;
using Entriqa.Domain.Errors;
using Entriqa.Domain.Submissions;
using Entriqa.Domain.UseCases;

namespace Entriqa.Application.UseCases;

/// <summary>
/// The guaranteed fallback of the system (every 15 min via DevOps schedule):
/// 1. Sweep - pick up stalled deferred runs (browser gone, sendBeacon lost).
/// 2. Auto retry - try failed steps again up to AutoRetryMax; after that they belong to the admin.
/// 3. Retention - delete submissions after RetentionDays (PDF blobs included), unconfirmed DOI after UnconfirmedRetentionDays.
/// 4. Security tables - dispose of expired nonces and old rate-limit windows.
/// All idempotent and conflict tolerant: a parallel confirm or run simply wins.
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

        // 1 + 2: sweep and auto retry
        foreach (var s in await list.ListUnfinishedAsync(now.AddMinutes(-o.SweepAfterMinutes), 200, ct))
        {
            var failed = s.StepRuns.Where(r => r.Status == StepRunStatus.Failed).ToList();
            if (failed.Any(r => r.Attempts >= o.AutoRetryMax)) continue;    // exhausted - stays visible for the admin

            var v = await getVersion.ExecuteAsync(s.Slug, s.Version, ct);
            if (v is null) { log.LogWarning("Housekeeping: Version {Version} zu {Id} fehlt", s.Version, s.Id); continue; }

            var mode = failed.Count > 0 ? RunMode.Retry : RunMode.Deferred;
            await pipeline.RunAsync(s, v.Definition.Localize(s.Locale), v.Version, mode, null, ct);
            try
            {
                await save.ExecuteAsync(s, ct);
                if (mode == RunMode.Retry) retried++; else swept++;
            }
            catch (AppException ex) when (ex.ErrorCode == ErrorCodes.Conflict) { /* a parallel run was faster */ }
        }

        // 3: retention - blobs first, then the table entry (the other way round a crash would leave orphaned blobs without a pointer).
        foreach (var s in await list.ListExpiredAsync(now.AddDays(-o.RetentionDays), now.AddDays(-o.UnconfirmedRetentionDays), 500, ct))
        {
            foreach (var path in s.Artifacts.Values.Where(v => !v.Contains("://", StringComparison.Ordinal)))
                await artifacts.DeleteAsync(path, ct);                      // blob paths only; "download" is a URL
            await delete.ExecuteAsync(s, ct);
            deleted++;
        }

        // 3b: orphaned visitor uploads - uploaded but never submitted. Files that were taken over have
        //     long since moved to attachments/ and hang on the life cycle of their submission.
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
