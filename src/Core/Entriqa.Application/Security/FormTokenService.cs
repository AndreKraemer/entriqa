using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Entriqa.Domain.Errors;

namespace Entriqa.Application.Security;

/// <summary>
/// HMAC-signed, stateless tokens. The secret never leaves the server - that is the difference
/// to hashes computed on the client, where the salt sits in the JavaScript.
/// Format: base64url(kind|subject|issuedAtUnix|nonce) + "." + base64url(HMAC-SHA256).
/// </summary>
public sealed class FormTokenService
{
    public const string KindForm = "form", KindRun = "run", KindConfirm = "confirm";
    public const string KindConfirmTest = "confirm-test";   // #21: the confirm link inside a test mail - it confirms nothing

    private readonly byte[] _secret;
    private readonly TimeProvider _time;

    public FormTokenService(IOptions<EntriqaOptions> options, TimeProvider time)
    {
        var secret = options.Value.TokenSecret;
        if (string.IsNullOrWhiteSpace(secret) || secret.Length < 32)
            throw new InvalidOperationException("Entriqa:TokenSecret fehlt oder ist zu kurz (mind. 32 Zeichen).");
        _secret = Encoding.UTF8.GetBytes(secret);
        _time = time;
    }

    public string Issue(string kind, string subject)
    {
        var nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(12)).ToLowerInvariant();
        var payload = $"{kind}|{subject}|{_time.GetUtcNow().ToUnixTimeSeconds()}|{nonce}";
        return $"{B64(Encoding.UTF8.GetBytes(payload))}.{B64(Sign(payload))}";
    }

    public TokenPayload Validate(string? token, string expectedKind, string expectedSubject, TimeSpan minAge, TimeSpan maxAge)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 512)
            throw new SecurityTokenException(ErrorCodes.TokenInvalid, ErrorMessages.TokenMissing);
        var dot = token.IndexOf('.');
        if (dot <= 0) throw new SecurityTokenException(ErrorCodes.TokenInvalid, ErrorMessages.TokenInvalid);

        string payload;
        byte[] sig;
        try { payload = Encoding.UTF8.GetString(UnB64(token[..dot])); sig = UnB64(token[(dot + 1)..]); }
        catch (FormatException) { throw new SecurityTokenException(ErrorCodes.TokenInvalid, ErrorMessages.TokenInvalid); }

        if (!CryptographicOperations.FixedTimeEquals(sig, Sign(payload)))
            throw new SecurityTokenException(ErrorCodes.TokenInvalid, ErrorMessages.TokenInvalid);

        var parts = payload.Split('|');
        if (parts.Length != 4 || parts[0] != expectedKind || parts[1] != expectedSubject
            || !long.TryParse(parts[2], out var issuedUnix))
            throw new SecurityTokenException(ErrorCodes.TokenInvalid, ErrorMessages.TokenMismatch);

        var issued = DateTimeOffset.FromUnixTimeSeconds(issuedUnix);
        var age = _time.GetUtcNow() - issued;
        if (age < minAge) throw new SecurityTokenException(ErrorCodes.TokenTooEarly, ErrorMessages.TokenTooEarly);
        if (age > maxAge) throw new SecurityTokenException(ErrorCodes.TokenExpired, ErrorMessages.TokenExpired);

        return new TokenPayload(parts[0], parts[1], issued, parts[3]);
    }

    private byte[] Sign(string payload) => HMACSHA256.HashData(_secret, Encoding.UTF8.GetBytes(payload));
    private static string B64(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static byte[] UnB64(string s)
    {
        var p = s.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(p.PadRight(p.Length + (4 - p.Length % 4) % 4, '='));
    }
}

public sealed record TokenPayload(string Kind, string Subject, DateTimeOffset IssuedAt, string Nonce);
