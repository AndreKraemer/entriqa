using Entriqa.Application.Consent;
using Entriqa.Domain.Consent;
using Entriqa.Domain.UseCases;

namespace Entriqa.Application.UseCases;

/// <summary>
/// The admin's view onto the consent proofs (#2). Thin over <see cref="ConsentProofService"/>, which
/// stays the one place that knows how a proof comes into existence and how it ends.
///
/// Neither of these takes a form query, and that is the design, not an omission: the proof carries the
/// wording that was ticked, and reaching for the form's current text would break AC 5 quietly.
/// </summary>
internal sealed class ListConsentProofsUseCase(ConsentProofService proofs) : IListConsentProofsUseCase
{
    public async Task<IReadOnlyList<ConsentProofView>> ExecuteAsync(string email, CancellationToken ct = default)
    {
        // Newest first, decided here rather than in the store: the table returns a partition in row-key
        // order, which is the submission id and says nothing about when anything was submitted.
        var found = await proofs.ListForAsync(email, ct);
        return [.. found.OrderByDescending(p => p.SubmittedAt).Select(View)];
    }

    internal static ConsentProofView View(ConsentProof p) => new(
        p.Email, p.SubmissionId, p.Slug, p.Version, p.SubmittedAt, p.ConfirmedAt, p.ConsentText,
        p.IpHash, p.ConfirmedIpHash);
}

internal sealed class DeleteConsentProofUseCase(ConsentProofService proofs) : IDeleteConsentProofUseCase
{
    public Task<bool> ExecuteAsync(string email, string submissionId, string by, CancellationToken ct = default)
    {
        _ = proofs;                                         // skeleton, no body yet (#2)
        throw new NotImplementedException("#2");
    }
}
