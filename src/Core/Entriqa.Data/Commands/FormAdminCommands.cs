using System.Text.Json;
using Azure;
using Azure.Data.Tables;
using Entriqa.Application.Ports;
using Entriqa.Data.Entities;
using Entriqa.Domain.Forms;

namespace Entriqa.Data.Commands;

internal sealed class SaveFormDraftCommand(TableStorage storage, TimeProvider time) : ISaveFormDraftCommand
{
    public async Task ExecuteAsync(FormDefinition definition, string savedBy, CancellationToken ct = default)
    {
        var table = await storage.GetAsync("Forms");
        FormEntity form;
        try { form = (await table.GetEntityAsync<FormEntity>("form", definition.Slug, cancellationToken: ct)).Value; }
        catch (RequestFailedException ex) when (ex.Status == 404) { form = new FormEntity { RowKey = definition.Slug, Status = "draft" }; }

        form.Name = definition.Name;
        form.Type = definition.Type;
        form.DraftJson = JsonSerializer.Serialize(definition, TableStorage.Json);
        form.UpdatedAt = time.GetUtcNow();
        form.UpdatedBy = savedBy;
        await table.UpsertEntityAsync(form, TableUpdateMode.Replace, ct);
    }
}
