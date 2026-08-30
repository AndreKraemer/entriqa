namespace Entriqa.Domain.Consent;

/// <summary>
/// The evidence for one consent (Art. 7(1) GDPR), deliberately kept apart from the submission (#1):
/// the submission dies with its retention period, the proof lives until the consent itself ends
/// (revocation, erasure request, contact deletion). It therefore carries the evidence and nothing
/// else - never field values, quiz outcomes or attachments.
/// </summary>
public sealed class ConsentProof
{
    public required string Email { get; init; }            // plaintext: the proof has to be attributable, and findable again for erasure
    public required string SubmissionId { get; init; }
    public required string Slug { get; init; }
    public required int Version { get; init; }
    public required DateTimeOffset SubmittedAt { get; init; }
    public required string ConsentText { get; init; }      // the exact wording the visitor agreed to
    public string? IpHash { get; init; }
    public DateTimeOffset? ConfirmedAt { get; init; }
    public string? ConfirmedIpHash { get; init; }

    /// <summary>
    /// How an address is compared and filed. Addresses match case-insensitively, so everything that
    /// has to find the same proof again - the storage key and the erasure audit hash - normalizes here
    /// and nowhere else. The table-key sanitation sits on top of this, in the data layer.
    /// </summary>
    public static string KeyOf(string email) => email.Trim().ToLowerInvariant();
}
