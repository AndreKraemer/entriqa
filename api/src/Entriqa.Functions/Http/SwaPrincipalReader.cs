using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Entriqa.Application;
using Entriqa.Domain.Errors;

namespace Entriqa.Functions.Http;

/// <summary>
/// Static Web Apps passes the signed-in user through as base64 JSON in the x-ms-client-principal header.
/// The route protection in staticwebapp.config.json is not enough for the API - the role is checked again here.
/// </summary>
public sealed class SwaPrincipalReader(IOptions<EntriqaOptions> options)
{
    public void RequireRole(HttpRequest req, string role)
    {
        if (options.Value.AllowAnonymousAdmin) return;                       // local only, without the SWA CLI
        if (!req.Headers.TryGetValue("x-ms-client-principal", out var header) || string.IsNullOrEmpty(header))
            throw new ForbiddenException(ErrorMessages.NotSignedIn);
        try
        {
            using var doc = JsonDocument.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(header!)));
            var roles = doc.RootElement.TryGetProperty("userRoles", out var r) ? r.EnumerateArray().Select(x => x.GetString()).ToList() : new();
            if (!roles.Contains(role)) throw new ForbiddenException(ErrorMessages.RoleRequired, AppException.Args("role", role));
        }
        catch (Exception ex) when (ex is FormatException or JsonException) { throw new ForbiddenException(ErrorMessages.PrincipalInvalid); }
    }

    /// <summary>Display name of the signed-in admin (userDetails) - for UpdatedBy and PublishedBy.</summary>
    public string UserName(HttpRequest req)
    {
        if (!req.Headers.TryGetValue("x-ms-client-principal", out var header) || string.IsNullOrEmpty(header)) return "admin";
        try
        {
            using var doc = JsonDocument.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(header!)));
            return doc.RootElement.TryGetProperty("userDetails", out var d) && d.GetString() is { Length: > 0 } name ? name : "admin";
        }
        catch (Exception ex) when (ex is FormatException or JsonException) { return "admin"; }
    }

    public static string? ClientIp(HttpRequest req)
    {
        // Azure appends on the right: only the last entry is trustworthy, everything before it is client input.
        // Entries can be "ip:port", "[v6]:port" or bare IPs (IPv6 included) - hence parsing instead of splitting.
        var xff = req.Headers["x-forwarded-for"].FirstOrDefault();
        if (!string.IsNullOrEmpty(xff))
        {
            var last = xff.Split(',')[^1].Trim();
            if (System.Net.IPEndPoint.TryParse(last, out var ep)) return ep.Address.ToString();
            if (System.Net.IPAddress.TryParse(last, out var ip)) return ip.ToString();
        }
        return req.HttpContext.Connection.RemoteIpAddress?.ToString();
    }
}
