using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Entriqa.Application.Ports;
using Entriqa.Application.Security;
using Entriqa.Domain.Consent;
using Entriqa.Domain.Forms;
using Entriqa.Domain.Submissions;

namespace Entriqa.Application.Consent;

/// <summary>
/// Writes, amends and ends the consent proof (#1). The one place that knows when a proof comes into
/// existence, what it may carry and what ends it - the use cases only call in at their events.
///
/// The proof has its own clock: the submission dies with RetentionDays, the proof is ended by an
/// event - the GDPR erasure of the contact - and only by a timer where one is deliberately configured.
/// </summary>
public sealed class ConsentProofService(
    IStoreConsentProofCommand store,
    IConfirmConsentProofCommand confirm,
    IDeleteConsentProofsByEmailCommand deleteByEmail,
    IRecordConsentDeletionCommand recordDeletion,
    IListExpiredConsentProofsQuery listExpired,
    IDeleteConsentProofCommand delete,
    IpHasher hasher,
    IOptions<EntriqaOptions> options,
    TimeProvider time,
    ILogger<ConsentProofService> log)
{
    /// <summary>AC 1/3/4: a proof for a ticked consent, carrying the evidence and nothing else.</summary>
    public async Task RecordAsync(FormDefinition def, Submission submission, CancellationToken ct = default)
    {
        // Read from the submission, never from a second dictionary: the rule has to be evaluated
        // against exactly the values that were stored, or the proof can disagree with what it proves.
        if (!def.ConsentGiven(submission.Values)) return;
        if (submission.Email is not { Length: > 0 } email) return;      // a proof nobody can be attributed to is not evidence

        var proof = new ConsentProof
        {
            Email = email,
            SubmissionId = submission.Id,
            Slug = submission.Slug,
            Version = submission.Version,
            SubmittedAt = submission.CreatedAt,
            ConsentText = submission.ConsentText ?? "",
            IpHash = submission.IpHash,
        };

        // Never at the cost of the submission: a table timeout must not turn a valid entry into an error.
        try { await store.ExecuteAsync(proof, ct); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogError(ex, "Einwilligungsnachweis für {Id} konnte nicht geschrieben werden", submission.Id);
        }
    }

    /// <summary>AC 2: the double opt-in confirmation amends an existing proof - it never creates one.</summary>
    public async Task ConfirmAsync(Submission submission, CancellationToken ct = default)
    {
        if (submission.ConfirmedAt is not { } confirmedAt) return;

        // A submission whose consent was never ticked has no proof, and a confirmation is not a
        // second consent - so this amends what is there and writes nothing where there is nothing.
        try { await confirm.ExecuteAsync(submission.Id, confirmedAt, submission.ConfirmedIpHash, ct); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogError(ex, "Einwilligungsnachweis für {Id} konnte nicht ergänzt werden", submission.Id);
        }
    }

    /// <summary>AC 7: GDPR erasure of a contact, plus the audit trail of that erasure.</summary>
    public async Task<int> DeleteForAsync(string email, CancellationToken ct = default)
    {
        // On the address, not on the submissions: after the retention period the contact has none
        // left, and the proof is exactly what the erasure has to reach.
        var count = await deleteByEmail.ExecuteAsync(email, ct);

        // Recorded even when nothing was found, and without the plaintext address. A missing row
        // would otherwise be ambiguous between "never had a proof" and "the erasure never ran" -
        // and the point of this row is to answer that question years later. Which is also why the
        // hash is taken over the normalized address: deletion matches case-insensitively, so a row
        // filed under the casing the admin happened to pass could never be found again.
        // The row is only as unguessable as EntriqaOptions.IpHashSalt - addresses are enumerable.
        try
        {
            await recordDeletion.ExecuteAsync(time.GetUtcNow(), hasher.Hash(ConsentProof.KeyOf(email)) ?? "", count, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The proofs are already gone at this point; losing the audit row must not report the
            // erasure itself as failed, but it is the one failure here worth an error.
            log.LogError(ex, "Löschvermerk für {Count} Einwilligungsnachweise konnte nicht geschrieben werden", count);
        }
        log.LogInformation("Einwilligungsnachweise gelöscht: {Count}", count);
        return count;
    }

    /// <summary>AC 5/8: nothing happens at the default of ConsentRetentionDays = 0.</summary>
    public async Task<int> PurgeExpiredAsync(DateTimeOffset now, CancellationToken ct = default)
    {
        // The unlimited default is the point, not an oversight: the duty to prove a consent runs for
        // as long as the consent is used, and Entriqa does not know when a contact leaves the CRM.
        // A site that does know may put a clock on it - nobody else gets one.
        var days = options.Value.ConsentRetentionDays;
        if (days <= 0) return 0;

        var expired = await listExpired.ExecuteAsync(now.AddDays(-days), 500, ct);
        foreach (var proof in expired) await delete.ExecuteAsync(proof, ct);
        if (expired.Count > 0)
            log.LogInformation("Housekeeping: {Count} Einwilligungsnachweise nach {Days} Tagen gelöscht", expired.Count, days);
        return expired.Count;
    }
}
