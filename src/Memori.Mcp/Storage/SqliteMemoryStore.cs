using Memori.Mcp.Models;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.VectorData;
using System.Linq.Expressions;

namespace Memori.Mcp.Storage;

public sealed class SqliteMemoryStore : IMemoryStore
{
    readonly VectorStoreCollection<string, McpFactRecord> factCollection;
    readonly IEmbeddingGenerator<string, Embedding<float>>? embeddingGenerator;

    public SqliteMemoryStore(
        VectorStoreCollection<string, McpFactRecord> factCollection,
        IEmbeddingGenerator<string, Embedding<float>>? embeddingGenerator = null)
    {
        this.factCollection = factCollection;
        this.embeddingGenerator = embeddingGenerator;
    }

    public async Task InsertAsync(McpFactRecord record)
    {
        if (embeddingGenerator is not null)
        {
            var generated = await embeddingGenerator.GenerateAsync(record.Content).ConfigureAwait(false);
            record.Embedding = generated.Vector;
        }

        await factCollection.UpsertAsync(record).ConfigureAwait(false);
    }

    public async Task<McpFactRecord?> GetAsync(string id)
    {
        return await factCollection.GetAsync(id).ConfigureAwait(false);
    }

    public async IAsyncEnumerable<McpFactRecord> ListAsync(string entityId, int skip, int limit, bool includeDeleted)
    {
        var filterOptions = new FilteredRecordRetrievalOptions<McpFactRecord> { Skip = skip };
        Expression<Func<McpFactRecord, bool>> filter = includeDeleted
            ? r => r.EntityId == entityId
            : r => r.EntityId == entityId && !r.IsDeleted;

        await foreach (var record in factCollection.GetAsync(filter, limit, filterOptions).ConfigureAwait(false))
            yield return record;
    }

    public async IAsyncEnumerable<(McpFactRecord Record, double Score)> SearchAsync(string query, string entityId, int limit)
    {
        var searchOptions = new VectorSearchOptions<McpFactRecord>
        {
            Filter = (McpFactRecord r) => r.EntityId == entityId && !r.IsDeleted
        };

        if (embeddingGenerator is not null)
        {
            var generated = await embeddingGenerator.GenerateAsync(query).ConfigureAwait(false);
            await foreach (var result in factCollection.SearchAsync(generated.Vector, limit, searchOptions).ConfigureAwait(false))
                yield return (result.Record, result.Score ?? 0);
        }
        else
        {
            await foreach (var result in factCollection.SearchAsync(query, limit, searchOptions).ConfigureAwait(false))
                yield return (result.Record, result.Score ?? 0);
        }
    }

    public async Task ReplaceAsync(McpFactRecord record)
    {
        if (embeddingGenerator is not null)
        {
            var generated = await embeddingGenerator.GenerateAsync(record.Content).ConfigureAwait(false);
            record.Embedding = generated.Vector;
        }

        await factCollection.UpsertAsync(record).ConfigureAwait(false);
    }

    public async Task<bool> DeleteAsync(string id, string entityId)
    {
        var record = await factCollection.GetAsync(id).ConfigureAwait(false);
        if (record is null || record.EntityId != entityId)
            return false;

        record.IsDeleted = true;
        await factCollection.UpsertAsync(record).ConfigureAwait(false);
        return true;
    }

    public async Task<int> ClearAsync(string entityId)
    {
        var count = 0;
        var filterOptions = new FilteredRecordRetrievalOptions<McpFactRecord>();

        await foreach (var record in factCollection.GetAsync(
            (McpFactRecord r) => r.EntityId == entityId && !r.IsDeleted, int.MaxValue, filterOptions).ConfigureAwait(false))
        {
            record.IsDeleted = true;
            await factCollection.UpsertAsync(record).ConfigureAwait(false);
            count++;
        }

        return count;
    }
}
