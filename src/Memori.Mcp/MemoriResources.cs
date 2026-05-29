using Memori.Mcp.Storage;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text.Json;

namespace Memori.Mcp;

[McpServerResourceType]
public sealed class MemoriResources
{
    readonly IMemoryStore store;
    readonly MemoriMcpOptions options;

    public MemoriResources(
        IMemoryStore store,
        IOptions<MemoriMcpOptions> options)
    {
        this.store = store;
        this.options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    }

    [McpServerResource(UriTemplate = "memori://facts", Name = "Memori Facts", MimeType = "application/json")]
    [Description("Returns all stored facts for the default entity as JSON")]
    public async Task<string> GetFactsAsync(CancellationToken ct = default)
    {
        try
        {
            var results = new List<object>();

            await foreach (var record in store.ListAsync(options.DefaultEntityId, 0, int.MaxValue, false).ConfigureAwait(false))
            {
                results.Add(new
                {
                    id = record.Id,
                    content = record.Content,
                    type = record.MemoryType,
                    confidence = record.Confidence,
                    createdAt = record.CreatedAt
                });
            }

            return JsonSerializer.Serialize(results);
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { error = ex.Message });
        }
    }

    [McpServerResource(UriTemplate = "memori://stats", Name = "Memori Stats", MimeType = "application/json")]
    [Description("Returns memory count, storage mode, and storage path")]
    public async Task<string> GetStatsAsync(CancellationToken ct = default)
    {
        try
        {
            var count = 0;

            await foreach (var _ in store.ListAsync(options.DefaultEntityId, 0, int.MaxValue, false).ConfigureAwait(false))
                count++;

            return JsonSerializer.Serialize(new
            {
                status = "ok",
                mode = options.Mode.ToString().ToLowerInvariant(),
                storagePath = options.StoragePath ?? (options.Mode == MemoriMode.Sqlite ? ".memori/memori.db" : ".memori/memories"),
                memoryCount = count,
                version = options.Version
            });
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { error = ex.Message });
        }
    }
}
