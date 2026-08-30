using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace Entriqa.Application.Security;

/// <summary>
/// Store an identifier hashed only - the IP for the rate limit and the DOI evidence, the address for the
/// erasure audit of a consent proof (#1). Data minimization; salt from the settings.
/// The salt is what makes this irreversible: both IPs and addresses are enumerable domains, so an empty
/// or guessable Entriqa__IpHashSalt turns every one of these hashes into a lookup.
/// </summary>
public sealed class IpHasher(IOptions<EntriqaOptions> options)
{
    public string? Hash(string? ip)
    {
        if (string.IsNullOrWhiteSpace(ip)) return null;
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(ip + "|" + options.Value.IpHashSalt));
        return Convert.ToHexString(bytes)[..32].ToLowerInvariant();
    }
}
