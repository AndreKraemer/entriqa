using Entriqa.Data.Entities;
using Entriqa.Domain.Consent;

namespace Entriqa.Data.Mapping;

/// <summary>Customs post for the consent proof (#1) - like SubmissionMapper, hand written, no JSON columns.</summary>
internal static class ConsentProofMapper
{
    /// <summary>
    /// The partition is the address: the erasure of a contact has to be a point query, not a scan.
    /// Table keys must not carry / \ # ? or control characters - an address that does gets them replaced.
    /// </summary>
    private const string ForbiddenKeyChars = @"/\#?";

    public static string PartitionOf(string email)
    {
        var key = ConsentProof.KeyOf(email);
        return string.Create(key.Length, key, (span, source) =>
        {
            for (var i = 0; i < source.Length; i++)
                span[i] = ForbiddenKeyChars.Contains(source[i], StringComparison.Ordinal)
                          || char.IsControl(source[i]) ? '_' : source[i];
        });
    }

    public static ConsentProofEntity ToEntity(ConsentProof p) => new()
    {
        PartitionKey = PartitionOf(p.Email),
        RowKey = p.SubmissionId,
        Email = p.Email,
        Slug = p.Slug,
        Version = p.Version,
        SubmittedAt = p.SubmittedAt,
        ConsentText = p.ConsentText,
        IpHash = p.IpHash,
        ConfirmedAt = p.ConfirmedAt,
        ConfirmedIpHash = p.ConfirmedIpHash,
    };

    public static ConsentProof ToDomain(ConsentProofEntity e) => new()
    {
        Email = e.Email,
        SubmissionId = e.RowKey,
        Slug = e.Slug,
        Version = e.Version,
        SubmittedAt = e.SubmittedAt,
        ConsentText = e.ConsentText,
        IpHash = e.IpHash,
        ConfirmedAt = e.ConfirmedAt,
        ConfirmedIpHash = e.ConfirmedIpHash,
    };
}
