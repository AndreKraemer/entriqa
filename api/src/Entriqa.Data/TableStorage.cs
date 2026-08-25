using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.Data.Tables;
using Microsoft.Extensions.Options;
using Entriqa.Application;

namespace Entriqa.Data;

/// <summary>Access to the tables. Clients are thread-safe and created once; CreateIfNotExists on first access.</summary>
public sealed class TableStorage
{
    private readonly TableServiceClient _service;
    private readonly string _prefix;
    private readonly Lazy<Task> _ensure;

    public TableStorage(IOptions<EntriqaOptions> options)
    {
        _service = new TableServiceClient(options.Value.Storage.ConnectionString);
        _prefix = options.Value.Storage.TablePrefix;
        _ensure = new Lazy<Task>(async () =>
        {
            foreach (var n in new[] { "Forms", "Versions", "Submissions", "Nonces", "RateLimits", "AdminState", "Funnel" })
                await _service.CreateTableIfNotExistsAsync(_prefix + n);
        });
    }

    public async Task<TableClient> GetAsync(string name) { await _ensure.Value; return _service.GetTableClient(_prefix + name); }

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };
}
