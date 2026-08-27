using System.Globalization;
using System.Text.Json;
using Azure;
using Azure.Data.Tables;
using Entriqa.Application.Ports;
using Entriqa.Data.Entities;
using Entriqa.Domain.Forms;

namespace Entriqa.Data.Commands;

/// <summary>Writes a new version snapshot and moves the pointer in the Forms entry. Two writes, deliberately in this order (version first - an orphaned snapshot is harmless).</summary>
internal sealed class PublishFormVersionCommand(TableStorage storage, TimeProvider time) : IPublishFormVersionCommand
{
    public async Task<FormVersion> ExecuteAsync(FormDefinition definition, string publishedBy, CancellationToken ct = default)
    {
        var forms = await storage.GetAsync("Forms");
        var versions = await storage.GetAsync("Versions");
        var now = time.GetUtcNow();

        FormEntity form;
        try { form = (await forms.GetEntityAsync<FormEntity>("form", definition.Slug, cancellationToken: ct)).Value; }
        catch (RequestFailedException ex) when (ex.Status == 404) { form = new FormEntity { RowKey = definition.Slug }; }

        var next = form.PublishedVersion + 1;
        var json = JsonSerializer.Serialize(definition, TableStorage.Json);
        await versions.UpsertEntityAsync(new FormVersionEntity
        {
            PartitionKey = definition.Slug, RowKey = next.ToString("D4", CultureInfo.InvariantCulture), DefinitionJson = json, PublishedAt = now, PublishedBy = publishedBy,
        }, TableUpdateMode.Replace, ct);

        form.Name = definition.Name; form.Type = definition.Type; form.Status = "published";
        form.DraftJson = json; form.PublishedVersion = next; form.UpdatedAt = now; form.UpdatedBy = publishedBy;
        await forms.UpsertEntityAsync(form, TableUpdateMode.Replace, ct);

        return new FormVersion(definition.Slug, next, definition, now, publishedBy);
    }
}
