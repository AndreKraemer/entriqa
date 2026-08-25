using System.Net.Http.Json;
using System.Text.Json;
using Entriqa.Application.Ports;
using Entriqa.Domain.Errors;

namespace Entriqa.Infrastructure.ReportingCloud;

/// <summary>
/// TX Text Control ReportingCloud: POST /v1/document/merge?templateName=…&returnFormat=PDF
/// The response is a JSON array of base64 documents (one per merge record).
/// Dokumentation: https://docs.reporting.cloud/docs/endpoint/document/merge
/// </summary>
public sealed class ReportingCloudAdapter(HttpClient http) : IMergeDocumentPort, IListReportTemplatesPort
{
    public async Task<IReadOnlyList<string>> ExecuteAsync(CancellationToken ct = default)
    {
        using var response = await http.GetAsync("templates/list", ct);
        if (!response.IsSuccessStatusCode)
            throw new InfrastructureException($"ReportingCloud: HTTP {(int)response.StatusCode}");
        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        return doc.RootElement.EnumerateArray()
            .Select(t => t.TryGetProperty("templateName", out var n) ? n.GetString() ?? "" : "")
            .Where(n => n.Length > 0).ToList();
    }

    public async Task<byte[]> MergeToPdfAsync(string templateName, object mergeData, CancellationToken ct = default)
    {
        var url = $"document/merge?templateName={Uri.EscapeDataString(templateName)}&returnFormat=PDF&append=false";
        var body = new { mergeData = new[] { mergeData }, mergeSettings = new { removeEmptyFields = true, removeEmptyBlocks = true } };
        using var response = await http.PostAsJsonAsync(url, body, ct);
        if (!response.IsSuccessStatusCode)
            throw new InfrastructureException($"ReportingCloud: HTTP {(int)response.StatusCode} {await response.Content.ReadAsStringAsync(ct)}");
        var docs = await response.Content.ReadFromJsonAsync<string[]>(cancellationToken: ct);
        if (docs is null || docs.Length == 0) throw new InfrastructureException("ReportingCloud: leere Antwort.");
        return Convert.FromBase64String(docs[0]);
    }
}
