using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Entriqa.Application;
using Entriqa.Application.Ports;
using Entriqa.Domain.Errors;

namespace Entriqa.Infrastructure.Brevo;

/// <summary>
/// Brevo REST API v3. Two calls are enough: transactional mail with template and parameters (plus attachment) and contact upsert.
/// Dokumentation: https://developers.brevo.com/reference/sendtransacemail, https://developers.brevo.com/reference/createcontact
/// </summary>
public sealed class BrevoAdapter(HttpClient http, IOptions<EntriqaOptions> options) : ISendTransactionalMailPort, IUpsertBrevoContactPort, IUpsertBrevoCompanyPort, IBrevoDirectoryPort
{
    /// <summary>
    /// Company upkeep in the Brevo CRM: look the company up by name filter, create it otherwise, then link the contact.
    /// Linking is idempotent - a contact that is already linked simply stays linked.
    /// Dokumentation: https://developers.brevo.com/reference/get_companies
    /// </summary>
    public async Task UpsertCompanyAsync(string name, string contactEmail, CancellationToken ct = default)
    {
        var companyId = await FindCompanyIdAsync(name, ct);
        if (companyId is null)
        {
            using var created = await http.PostAsJsonAsync("companies", new { name }, ct);
            await EnsureOk(created, "Brevo Companies", ct);
            using var doc = await JsonDocument.ParseAsync(await created.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            companyId = doc.RootElement.GetProperty("id").GetString();
        }

        // Linking needs the numeric contact id - look it up by email (the contact exists
        // thanks to the brevo.contact step before it).
        using var contactDoc = await GetJson($"contacts/{Uri.EscapeDataString(contactEmail)}", ct);
        var contactId = contactDoc.RootElement.GetProperty("id").GetInt64();

        using var linked = await http.PatchAsJsonAsync($"companies/link-unlink/{companyId}", new { linkContactIds = new[] { contactId } }, ct);
        await EnsureOk(linked, "Brevo Companies", ct);
    }

    private async Task<string?> FindCompanyIdAsync(string name, CancellationToken ct)
    {
        var filters = Uri.EscapeDataString(JsonSerializer.Serialize(new Dictionary<string, string> { ["attributes.name"] = name }));
        using var doc = await GetJson($"companies?filters={filters}&limit=10", ct);
        if (!doc.RootElement.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array) return null;
        foreach (var item in items.EnumerateArray())
        {
            var itemName = item.TryGetProperty("attributes", out var attrs) && attrs.TryGetProperty("name", out var n) ? n.GetString() : null;
            if (string.Equals(itemName, name, StringComparison.OrdinalIgnoreCase))
                return item.GetProperty("id").GetString();
        }
        return null;
    }

    public async Task<IReadOnlyList<BrevoListInfo>> GetListsAsync(CancellationToken ct = default)
    {
        using var doc = await GetJson("contacts/lists?limit=50", ct);
        return doc.RootElement.TryGetProperty("lists", out var lists)
            ? lists.EnumerateArray().Select(l => new BrevoListInfo(l.GetProperty("id").GetInt64(), l.GetProperty("name").GetString() ?? "")).ToList()
            : Array.Empty<BrevoListInfo>();
    }

    public async Task<IReadOnlyList<BrevoTemplateInfo>> GetTemplatesAsync(CancellationToken ct = default)
    {
        using var doc = await GetJson("smtp/templates?templateStatus=true&limit=100", ct);
        return doc.RootElement.TryGetProperty("templates", out var templates)
            ? templates.EnumerateArray().Select(t => new BrevoTemplateInfo(t.GetProperty("id").GetInt64(), t.GetProperty("name").GetString() ?? "")).ToList()
            : Array.Empty<BrevoTemplateInfo>();
    }

    private async Task<JsonDocument> GetJson(string url, CancellationToken ct)
    {
        using var response = await http.GetAsync(url, ct);
        await EnsureOk(response, "Brevo", ct);
        return await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
    }

    public async Task SendAsync(string toEmail, string? toName, int templateId, IReadOnlyDictionary<string, object?> parameters, MailAttachment? attachment = null, CancellationToken ct = default)
    {
        var o = options.Value.Brevo;
        var body = new Dictionary<string, object?>
        {
            ["sender"] = new { name = o.SenderName, email = o.SenderEmail },
            ["to"] = new[] { new { email = toEmail, name = toName } },
            ["templateId"] = templateId,
            ["params"] = parameters,
        };
        if (attachment is not null)
            body["attachment"] = new[] { new { name = attachment.FileName, content = Convert.ToBase64String(attachment.Content) } };

        using var response = await http.PostAsJsonAsync("smtp/email", body, ct);
        await EnsureOk(response, "Brevo SMTP", ct);
    }

    public async Task<string?> UpsertAsync(string email, IReadOnlyList<int> listIds, IReadOnlyDictionary<string, object?> attributes, CancellationToken ct = default)
    {
        var body = new { email, listIds, attributes, updateEnabled = true };
        using var response = await http.PostAsJsonAsync("contacts", body, ct);
        await EnsureOk(response, "Brevo Contacts", ct);
        if (response.Content.Headers.ContentLength is > 0)
        {
            try
            {
                using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
                if (doc.RootElement.TryGetProperty("id", out var id)) return id.ToString();
            }
            catch (JsonException) { /* 204 without a body on update - not an error */ }
        }
        return null;
    }

    private static async Task EnsureOk(HttpResponseMessage response, string what, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode) return;
        var text = await response.Content.ReadAsStringAsync(ct);
        throw new InfrastructureException($"{what}: HTTP {(int)response.StatusCode} {Short(text)}");
    }

    private static string Short(string s) => s.Length <= 300 ? s : s[..300];
}
