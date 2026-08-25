using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace Entriqa.Application.Security;

/// <summary>Store the IP hashed only (rate limit, DOI evidence). Data minimization; salt from the settings.</summary>
public sealed class IpHasher(IOptions<EntriqaOptions> options)
{
    public string? Hash(string? ip)
    {
        if (string.IsNullOrWhiteSpace(ip)) return null;
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(ip + "|" + options.Value.IpHashSalt));
        return Convert.ToHexString(bytes)[..32].ToLowerInvariant();
    }
}
