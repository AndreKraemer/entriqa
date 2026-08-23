using System.Text.Json;
using Azure;
using Entriqa.Application.Ports;
using Entriqa.Data.Entities;
using Entriqa.Domain.Forms;
using Entriqa.Domain.UseCases;

namespace Entriqa.Data.Queries;

internal sealed class ListFormsQuery(TableStorage storage) : IListFormsQuery
{
    public async Task<IReadOnlyList<FormListItem>> ExecuteAsync(CancellationToken ct = default)
    {
        var table = await storage.GetAsync("Forms");
        var result = new List<FormListItem>();
        await foreach (var e in table.QueryAsync<FormEntity>(e => e.PartitionKey == "form", cancellationToken: ct))
            result.Add(new FormListItem(e.RowKey, e.Name, e.Type, e.Status, e.PublishedVersion, e.UpdatedAt));
        return result.OrderBy(f => f.Slug).ToList();
    }
}

internal sealed class TryGetFormDraftQuery(TableStorage storage) : ITryGetFormDraftQuery
{
    public async Task<FormDraft?> ExecuteAsync(string slug, CancellationToken ct = default)
    {
        var table = await storage.GetAsync("Forms");
        try
        {
            var e = (await table.GetEntityAsync<FormEntity>("form", slug, cancellationToken: ct)).Value;
            var def = JsonSerializer.Deserialize<FormDefinition>(e.DraftJson, TableStorage.Json)
                ?? throw new InvalidOperationException($"Entwurf '{slug}' ist leer.");
            return new FormDraft(e.RowKey, e.Status, e.PublishedVersion, e.UpdatedAt, e.UpdatedBy, def);
        }
        catch (RequestFailedException ex) when (ex.Status == 404) { return null; }
    }
}
