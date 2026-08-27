using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Entriqa.Application.Security;

/// <summary>
/// Offline check of the license key (app setting <c>Entriqa__LicenseKey</c>).
/// Format: <c>ENTRIQA-&lt;base64url(payload)&gt;.&lt;base64url(signatur)&gt;</c>,
/// Payload JSON <c>{"id","plan","until"}</c>, signature ECDSA P-256/SHA-256 over the payload bytes.
/// No phone home, no kill switch: without a valid key everything keeps working,
/// the admin permanently shows the unlicensed notice (the Kirby model).
/// Issuing keys: src/Hosts/Entriqa.KeyTool (the private key stays with the vendor).
/// </summary>
public sealed class LicenseService(IOptions<EntriqaOptions> options, TimeProvider time)
{
    /// <summary>SPKI (base64) of the vendor key - its counterpart is NOT in the repository.</summary>
    public const string VendorPublicKey = "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEHk/XOpxIc50g4DM9EePg/ym9to0xYcNd4ML43OtrTWesoPCTQFN9vQ7kl29R8RLlDIEDr54n7HjgessOiCuqPA==";

    public LicenseInfo Check() => Validate(options.Value.LicenseKey, VendorPublicKey, time.GetUtcNow());

    public static LicenseInfo Validate(string? key, string publicKeySpkiBase64, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(key)) return LicenseInfo.Missing;
        try
        {
            if (!key.StartsWith("ENTRIQA-", StringComparison.Ordinal)) return LicenseInfo.Invalid;
            var parts = key["ENTRIQA-".Length..].Split('.');
            if (parts.Length != 2) return LicenseInfo.Invalid;
            var payload = FromBase64Url(parts[0]);
            var signature = FromBase64Url(parts[1]);

            using var ecdsa = ECDsa.Create();
            ecdsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(publicKeySpkiBase64), out _);
            if (!ecdsa.VerifyData(payload, signature, HashAlgorithmName.SHA256)) return LicenseInfo.Invalid;

            using var doc = JsonDocument.Parse(payload);
            var id = doc.RootElement.GetProperty("id").GetString() ?? "";
            var plan = doc.RootElement.GetProperty("plan").GetString() ?? "";
            var until = DateOnly.ParseExact(doc.RootElement.GetProperty("until").GetString() ?? "", "yyyy-MM-dd", CultureInfo.InvariantCulture);
            return until < DateOnly.FromDateTime(now.UtcDateTime)
                ? new LicenseInfo(false, id, plan, until, "expired")
                : new LicenseInfo(true, id, plan, until, "valid");
        }
        catch (Exception ex) when (ex is FormatException or JsonException or CryptographicException or KeyNotFoundException or InvalidOperationException)
        {
            return LicenseInfo.Invalid;
        }
    }

    public static string Issue(string privateKeyPkcs8Base64, string id, string plan, DateOnly until)
    {
        var payload = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { id, plan, until = until.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) }));
        using var ecdsa = ECDsa.Create();
        ecdsa.ImportPkcs8PrivateKey(Convert.FromBase64String(privateKeyPkcs8Base64), out _);
        var signature = ecdsa.SignData(payload, HashAlgorithmName.SHA256);
        return $"ENTRIQA-{ToBase64Url(payload)}.{ToBase64Url(signature)}";
    }

    private static string ToBase64Url(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static byte[] FromBase64Url(string s)
    {
        var t = s.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(t.PadRight(t.Length + (4 - t.Length % 4) % 4, '='));
    }
}

public sealed record LicenseInfo(bool Valid, string? Id, string? Plan, DateOnly? ValidUntil, string Status)
{
    public static readonly LicenseInfo Missing = new(false, null, null, null, "missing");
    public static readonly LicenseInfo Invalid = new(false, null, null, null, "invalid");
}
