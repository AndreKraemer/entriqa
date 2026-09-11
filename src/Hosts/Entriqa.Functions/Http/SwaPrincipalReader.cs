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

    /// <summary>
    /// Display name of the signed-in admin, or null when the request carried no usable principal (#14).
    /// Use this wherever the name is <em>attribution</em> - a history entry, UpdatedBy, PublishedBy, the
    /// erasure audit row. <see cref="UserName"/>'s literal "admin" cannot be told apart from a real
    /// account of that name, so it would record a person for something nobody signed in did.
    /// </summary>
    public string? TryUserName(HttpRequest req) => throw new NotImplementedException();

    /// <summary>
    /// Display name of the signed-in admin (userDetails), falling back to "admin".
    /// <para>This is the <em>identity key</em> of the per-admin state row, not an attribution: it is the
    /// RowKey of AdminStateEntity, so it has to be stable and non-null even without a principal. For
    /// anything that records who did something, use <see cref="TryUserName"/> (#14).</para>
    /// </summary>
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
