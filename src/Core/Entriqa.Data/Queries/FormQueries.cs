using System.Globalization;
using System.Text.Json;
using Azure;
using Entriqa.Application.Ports;
using Entriqa.Data.Entities;
using Entriqa.Domain.Forms;

namespace Entriqa.Data.Queries;

internal sealed class TryGetPublishedFormQuery(TableStorage storage) : ITryGetPublishedFormQuery
{
    public async Task<FormVersion?> ExecuteAsync(string slug, CancellationToken ct = default)
    {
        var forms = await storage.GetAsync("Forms");
        try
        {
            var form = (await forms.GetEntityAsync<FormEntity>("form", slug, cancellationToken: ct)).Value;
            if (form.Status != "published" || form.PublishedVersion <= 0) return null;
            return await new TryGetFormVersionQuery(storage).ExecuteAsync(slug, form.PublishedVersion, ct);
        }
        catch (RequestFailedException ex) when (ex.Status == 404) { return null; }
    }
}

internal sealed class TryGetFormVersionQuery(TableStorage storage) : ITryGetFormVersionQuery
{
    public async Task<FormVersion?> ExecuteAsync(string slug, int version, CancellationToken ct = default)
    {
        var versions = await storage.GetAsync("Versions");
        try
        {
            var e = (await versions.GetEntityAsync<FormVersionEntity>(slug, version.ToString("D4", CultureInfo.InvariantCulture), cancellationToken: ct)).Value;
            var def = JsonSerializer.Deserialize<FormDefinition>(e.DefinitionJson, TableStorage.Json)
                ?? throw new InvalidOperationException($"Definition {slug} v{version} ist leer.");
            return new FormVersion(slug, version, def, e.PublishedAt, e.PublishedBy);
        }
        catch (RequestFailedException ex) when (ex.Status == 404) { return null; }
    }
}
