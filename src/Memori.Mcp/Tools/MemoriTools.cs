using Memori.Mcp.Models;
using Memori.Mcp.Storage;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text.Json;

namespace Memori.Mcp.Tools;

[McpServerToolType]
public sealed class MemoriTools
{
    readonly IMemoryStore store;
    readonly MemoriMcpOptions options;
    string? currentEntityId;

    public MemoriTools(
        IMemoryStore store,
        IOptions<MemoriMcpOptions> options)
    {
        this.store = store ?? throw new ArgumentNullException(nameof(store));
        this.options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    }

    string entityId => currentEntityId ?? options.DefaultEntityId;

    [McpServerTool]
    [Description("Ping the server. Returns status, storage mode, storage path, and version — use to verify the server is ready.")]
    public string Ping()
    {
        return JsonSerializer.Serialize(new
        {
            status = "ok",
            mode = options.Mode.ToString().ToLowerInvariant(),
            storagePath = options.StoragePath ?? (options.Mode == MemoriMode.Sqlite ? ".memori/memori.db" : ".memori/memories"),
            version = options.Version
        });
    }

    [McpServerTool]
    [Description("Store a new fact about the current entity in durable memory for future recall")]
    public async Task<string> Remember(
        [Description("The fact content to remember")] string content,
        [Description("Optional memory type classification (e.g., preference, profile, fact)")] string? memoryType = null,
        [Description("Optional comma-separated tags to associate with this memory")] string? tags = null)
    {
        try
        {
            currentEntityId ??= options.DefaultEntityId;

            var record = new McpFactRecord
            {
                Id = Guid.NewGuid().ToString("N"),
                EntityId = entityId,
                Content = content,
                MemoryType = memoryType,
                Tags = string.IsNullOrWhiteSpace(tags) ? null : tags,
                Confidence = 1.0,
                CreatedAt = DateTimeOffset.UtcNow,
                Version = 1,
                IsDeleted = false
            };

            await store.InsertAsync(record).ConfigureAwait(false);

            return JsonSerializer.Serialize(new { status = "remembered", id = record.Id });
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { error = ex.Message });
        }
    }

    [McpServerTool]
    [Description("Search stored memories by semantic query, returning ranked results")]
    public async Task<string> Search(
        [Description("The search text to find in memory content")] string query,
        [Description("Maximum number of results to return (default 10)")] int? limit = null)
    {
        try
        {
            var results = new List<object>();
            var resolvedLimit = limit ?? 10;

            await foreach (var (record, score) in store.SearchAsync(query, entityId, resolvedLimit).ConfigureAwait(false))
            {
                results.Add(new
                {
                    id = record.Id,
                    content = record.Content,
                    score,
                    type = record.MemoryType,
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

    [McpServerTool]
    [Description("List all memories for the current entity with optional pagination")]
    public async Task<string> List(
        [Description("Number of records to skip (for pagination, default 0)")] int? skip = null,
        [Description("Maximum number of records to return (default 50)")] int? take = null)
    {
        try
        {
            var results = new List<object>();

            await foreach (var record in store.ListAsync(entityId, skip ?? 0, take ?? 50, false).ConfigureAwait(false))
            {
                results.Add(new
                {
                    id = record.Id,
                    content = record.Content,
                    type = record.MemoryType,
                    confidence = record.Confidence,
                    createdAt = record.CreatedAt,
                    isDeleted = record.IsDeleted
                });
            }

            return JsonSerializer.Serialize(results);
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { error = ex.Message });
        }
    }

    [McpServerTool]
    [Description("Get a specific memory record by its unique identifier")]
    public async Task<string> Get(
        [Description("The unique identifier of the memory record")] string memoryId)
    {
        try
        {
            var record = await store.GetAsync(memoryId).ConfigureAwait(false);

            if (record is null)
                return JsonSerializer.Serialize(new { error = $"Memory '{memoryId}' not found." });

            return JsonSerializer.Serialize(new
            {
                id = record.Id,
                content = record.Content,
                type = record.MemoryType,
                confidence = record.Confidence,
                createdAt = record.CreatedAt,
                isDeleted = record.IsDeleted,
                version = record.Version
            });
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { error = ex.Message });
        }
    }

    [McpServerTool]
    [Description("Update the content of an existing memory record")]
    public async Task<string> Update(
        [Description("The unique identifier of the memory record to update")] string memoryId,
        [Description("The new content text for the memory")] string content)
    {
        try
        {
            var record = await store.GetAsync(memoryId).ConfigureAwait(false);

            if (record is null)
                return JsonSerializer.Serialize(new { error = $"Memory '{memoryId}' not found." });

            record.Content = content;
            record.Version++;

            await store.ReplaceAsync(record).ConfigureAwait(false);

            return JsonSerializer.Serialize(new { status = "updated", id = memoryId });
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { error = ex.Message });
        }
    }

    [McpServerTool]
    [Description("Soft-delete a memory record by its unique identifier")]
    public async Task<string> Delete(
        [Description("The unique identifier of the memory record to delete")] string memoryId)
    {
        try
        {
            var found = await store.DeleteAsync(memoryId, entityId).ConfigureAwait(false);

            if (!found)
                return JsonSerializer.Serialize(new { error = $"Memory '{memoryId}' not found." });

            return JsonSerializer.Serialize(new { status = "deleted", id = memoryId });
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { error = ex.Message });
        }
    }

    [McpServerTool]
    [Description("Clear all memories for the current entity by soft-deleting each record")]
    public async Task<string> Clear()
    {
        try
        {
            var count = await store.ClearAsync(entityId).ConfigureAwait(false);

            return JsonSerializer.Serialize(new { status = "cleared", count });
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { error = ex.Message });
        }
    }
}
