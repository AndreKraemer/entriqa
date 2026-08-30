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
/// SKELETON: every method is a deliberate no-op until the implementation commit. The red tests on
/// the issue assert against the ports below and fail because nothing reaches them yet.
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
    public async Task RecordAsync(FormDefinition def, IReadOnlyDictionary<string, string> values, Submission submission,
                                  CancellationToken ct = default)
    {
        if (!def.ConsentGiven(values)) return;
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
    public Task ConfirmAsync(Submission submission, CancellationToken ct = default)
    {
        _ = (confirm, submission, ct);
        return Task.CompletedTask;
    }

    /// <summary>AC 7: GDPR erasure of a contact, plus the audit trail of that erasure.</summary>
    public Task<int> DeleteForAsync(string email, CancellationToken ct = default)
    {
        _ = (deleteByEmail, recordDeletion, hasher, time, email, ct);
        return Task.FromResult(0);
    }

    /// <summary>AC 5/8: nothing happens at the default of ConsentRetentionDays = 0.</summary>
    public Task<int> PurgeExpiredAsync(DateTimeOffset now, CancellationToken ct = default)
    {
        _ = (listExpired, delete, options, now, ct);
        return Task.FromResult(0);
    }
}
