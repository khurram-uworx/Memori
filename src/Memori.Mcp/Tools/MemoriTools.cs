using Memori.Mcp.Models;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.VectorData;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Linq.Expressions;
using System.Text.Json;

namespace Memori.Mcp.Tools;

/// <summary>
/// MCP tools for storing, searching, and managing durable memories directly via VectorStore.
/// </summary>
[McpServerToolType]
sealed class MemoriTools
{
    readonly VectorStoreCollection<string, McpFactRecord> factCollection;
    readonly IEmbeddingGenerator<string, Embedding<float>>? embeddingGenerator;
    readonly string defaultEntityId;
    string? currentEntityId;

    /// <summary>
    /// Creates memory tools backed by the given VectorStore collection.
    /// </summary>
    public MemoriTools(
        VectorStoreCollection<string, McpFactRecord> factCollection,
        IOptions<MemoriMcpOptions> options,
        IEmbeddingGenerator<string, Embedding<float>>? embeddingGenerator = null)
    {
        this.factCollection = factCollection ?? throw new ArgumentNullException(nameof(factCollection));
        this.embeddingGenerator = embeddingGenerator;
        defaultEntityId = options?.Value?.DefaultEntityId ?? "default";
    }

    string entityId => currentEntityId ?? defaultEntityId;

    static Expression<Func<McpFactRecord, bool>> ActiveFilter(string id) =>
        r => r.EntityId == id && !r.IsDeleted;

    /// <summary>
    /// Stores a new fact about the current entity in durable memory for future recall.
    /// </summary>
    [McpServerTool]
    [Description("Store a new fact about the current entity in durable memory for future recall")]
    public async Task<string> Remember(
        [Description("The fact content to remember")] string content,
        [Description("Optional memory type classification (e.g., preference, profile, fact)")] string? memoryType = null,
        [Description("Optional comma-separated tags to associate with this memory")] string? tags = null)
    {
        try
        {
            currentEntityId ??= defaultEntityId;

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

            if (embeddingGenerator is not null)
            {
                var generated = await embeddingGenerator.GenerateAsync(content).ConfigureAwait(false);
                record.Embedding = generated.Vector;
            }

            await factCollection.UpsertAsync(record).ConfigureAwait(false);

            return JsonSerializer.Serialize(new { status = "remembered", id = record.Id });
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Searches stored memories by semantic query, returning ranked results.
    /// </summary>
    [McpServerTool]
    [Description("Search stored memories by semantic query, returning ranked results")]
    public async Task<string> Search(
        [Description("The search text to find in memory content")] string query,
        [Description("Maximum number of results to return (default 10)")] int? limit = null)
    {
        try
        {
            var searchOptions = new VectorSearchOptions<McpFactRecord>
            {
                Filter = ActiveFilter(entityId)
            };

            var results = new List<object>();
            var resolvedLimit = limit ?? 10;

            if (embeddingGenerator is not null)
            {
                var generated = await embeddingGenerator.GenerateAsync(query).ConfigureAwait(false);
                await foreach (var result in factCollection.SearchAsync(
                    generated.Vector, resolvedLimit, searchOptions).ConfigureAwait(false))
                {
                    results.Add(new
                    {
                        id = result.Record.Id,
                        content = result.Record.Content,
                        score = result.Score,
                        type = result.Record.MemoryType,
                        createdAt = result.Record.CreatedAt
                    });
                }
            }
            else
            {
                await foreach (var result in factCollection.SearchAsync(
                    query, resolvedLimit, searchOptions).ConfigureAwait(false))
                {
                    results.Add(new
                    {
                        id = result.Record.Id,
                        content = result.Record.Content,
                        score = result.Score,
                        type = result.Record.MemoryType,
                        createdAt = result.Record.CreatedAt
                    });
                }
            }

            return JsonSerializer.Serialize(results);
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Lists all memories for the current entity with optional pagination.
    /// </summary>
    [McpServerTool]
    [Description("List all memories for the current entity with optional pagination")]
    public async Task<string> List(
        [Description("Number of records to skip (for pagination, default 0)")] int? skip = null,
        [Description("Maximum number of records to return (default 50)")] int? take = null)
    {
        try
        {
            var filterOptions = new FilteredRecordRetrievalOptions<McpFactRecord>
            {
                Skip = skip ?? 0
            };

            var results = new List<object>();
            await foreach (var record in factCollection.GetAsync(
                ActiveFilter(entityId), take ?? 50, filterOptions).ConfigureAwait(false))
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

    /// <summary>
    /// Gets a specific memory record by its unique identifier.
    /// </summary>
    [McpServerTool]
    [Description("Get a specific memory record by its unique identifier")]
    public async Task<string> Get(
        [Description("The unique identifier of the memory record")] string memoryId)
    {
        try
        {
            var record = await factCollection.GetAsync(memoryId).ConfigureAwait(false);

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

    /// <summary>
    /// Updates the content of an existing memory record.
    /// </summary>
    [McpServerTool]
    [Description("Update the content of an existing memory record")]
    public async Task<string> Update(
        [Description("The unique identifier of the memory record to update")] string memoryId,
        [Description("The new content text for the memory")] string content)
    {
        try
        {
            var record = await factCollection.GetAsync(memoryId).ConfigureAwait(false);

            if (record is null)
                return JsonSerializer.Serialize(new { error = $"Memory '{memoryId}' not found." });

            record.Content = content;
            record.Version++;

            await factCollection.UpsertAsync(record).ConfigureAwait(false);

            return JsonSerializer.Serialize(new { status = "updated", id = memoryId });
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Soft-deletes a memory record by its unique identifier.
    /// </summary>
    [McpServerTool]
    [Description("Soft-delete a memory record by its unique identifier")]
    public async Task<string> Delete(
        [Description("The unique identifier of the memory record to delete")] string memoryId)
    {
        try
        {
            var record = await factCollection.GetAsync(memoryId).ConfigureAwait(false);

            if (record is null)
                return JsonSerializer.Serialize(new { error = $"Memory '{memoryId}' not found." });

            record.IsDeleted = true;

            await factCollection.UpsertAsync(record).ConfigureAwait(false);

            return JsonSerializer.Serialize(new { status = "deleted", id = memoryId });
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Clears all memories for the current entity by soft-deleting each record.
    /// </summary>
    [McpServerTool]
    [Description("Clear all memories for the current entity by soft-deleting each record")]
    public async Task<string> Clear()
    {
        try
        {
            var memories = new List<McpFactRecord>();
            await foreach (var record in factCollection.GetAsync(
                ActiveFilter(entityId), int.MaxValue).ConfigureAwait(false))
            {
                memories.Add(record);
            }

            var count = 0;
            foreach (var memory in memories)
            {
                memory.IsDeleted = true;
                await factCollection.UpsertAsync(memory).ConfigureAwait(false);
                count++;
            }

            return JsonSerializer.Serialize(new { status = "cleared", count });
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { error = ex.Message });
        }
    }
}
