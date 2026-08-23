using System.Net.Http.Json;
using Entriqa.Application.Ports;
using Entriqa.Domain.Errors;

namespace Entriqa.Infrastructure.Http;

public sealed class WebhookAdapter(HttpClient http) : IPostWebhookPort
{
    public async Task PostJsonAsync(Uri url, object payload, CancellationToken ct = default)
    {
        using var response = await http.PostAsJsonAsync(url, payload, ct);
        if (!response.IsSuccessStatusCode) throw new InfrastructureException($"Webhook: HTTP {(int)response.StatusCode}");
    }
}
