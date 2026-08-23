using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Entriqa.Application;
using Entriqa.Domain.Errors;

namespace Entriqa.Functions.Http;

/// <summary>
/// Static Web Apps reicht den angemeldeten Benutzer als Base64-JSON im Header x-ms-client-principal durch.
/// Der Routenschutz in staticwebapp.config.json reicht für die API nicht – hier wird die Rolle nochmal geprüft.
/// </summary>
public sealed class SwaPrincipalReader(IOptions<EntriqaOptions> options)
{
    public void RequireRole(HttpRequest req, string role)
    {
        if (options.Value.AllowAnonymousAdmin) return;                       // nur lokal ohne SWA-CLI
        if (!req.Headers.TryGetValue("x-ms-client-principal", out var header) || string.IsNullOrEmpty(header))
            throw new ForbiddenException("Nicht angemeldet.");
        try
        {
            using var doc = JsonDocument.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(header!)));
            var roles = doc.RootElement.TryGetProperty("userRoles", out var r) ? r.EnumerateArray().Select(x => x.GetString()).ToList() : new();
            if (!roles.Contains(role)) throw new ForbiddenException($"Rolle '{role}' erforderlich.");
        }
        catch (Exception ex) when (ex is FormatException or JsonException) { throw new ForbiddenException("Ungültiger Principal."); }
    }

    /// <summary>Anzeigename des angemeldeten Admins (userDetails) – für UpdatedBy/PublishedBy.</summary>
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
        // Azure hängt rechts an: nur der letzte Eintrag ist vertrauenswürdig, alles davor ist Client-Eingabe.
        // Einträge können "ip:port", "[v6]:port" oder nackte IPs (auch IPv6) sein – deshalb parsen statt splitten.
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
