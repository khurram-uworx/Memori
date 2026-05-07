using Memori.Models;
using Microsoft.Extensions.VectorData;
using System.Linq.Expressions;

namespace Memori.Management;

/// <summary>
/// Default implementation of <see cref="IMemoryManagementService"/> backed by a
/// <see cref="VectorStoreCollection{TKey, TRecord}"/>.
/// </summary>
public sealed class MemoryManagementService : IMemoryManagementService
{
    readonly VectorStoreCollection<string, MemoryFactRecord> factCollection;

    /// <summary>
    /// Creates a memory management service.
    /// </summary>
    /// <param name="factCollection">The vector store collection backing memory facts.</param>
    public MemoryManagementService(
        VectorStoreCollection<string, MemoryFactRecord> factCollection)
    {
        this.factCollection = factCollection ?? throw new ArgumentNullException(nameof(factCollection));
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<MemoryFactRecord>> ListMemoriesAsync(
        string entityId,
        int skip = 0,
        int take = 50,
        CancellationToken cancellationToken = default)
    {
        var filterOptions = new FilteredRecordRetrievalOptions<MemoryFactRecord>
        {
            Skip = skip
        };

        var results = new List<MemoryFactRecord>();
        await foreach (var record in factCollection.GetAsync(
            r => r.EntityId == entityId && !r.IsDeleted,
            take,
            filterOptions,
            cancellationToken).ConfigureAwait(false))
        {
            results.Add(record);
        }

        return results;
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<MemoryFactRecord>> SearchMemoriesAsync(
        string entityId,
        string searchText,
        string? memoryType = null,
        string? scope = null,
        bool includeDeleted = false,
        int take = 50,
        CancellationToken cancellationToken = default)
    {
        Expression<Func<MemoryFactRecord, bool>> filter = BuildSearchFilter(
            entityId, memoryType, scope, includeDeleted);

        var searchOptions = new VectorSearchOptions<MemoryFactRecord>
        {
            Filter = filter
        };

        var results = new List<MemoryFactRecord>();
        await foreach (var result in factCollection.SearchAsync(
            searchText,
            take,
            searchOptions,
            cancellationToken).ConfigureAwait(false))
        {
            if (!string.IsNullOrWhiteSpace(result.Record.Content))
                results.Add(result.Record);
        }

        return results;
    }

    /// <inheritdoc />
    public async ValueTask<MemoryFactRecord?> GetMemoryAsync(
        string memoryId,
        CancellationToken cancellationToken = default)
    {
        return await factCollection.GetAsync(memoryId, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<bool> UpdateMemoryAsync(
        string memoryId,
        string newContent,
        CancellationToken cancellationToken = default)
    {
        var record = await factCollection.GetAsync(memoryId, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (record is null)
            return false;

        record.Content = newContent;
        record.Version++;

        await factCollection.UpsertAsync(record, cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <inheritdoc />
    public async ValueTask<bool> SoftDeleteMemoryAsync(
        string memoryId,
        CancellationToken cancellationToken = default)
    {
        var record = await factCollection.GetAsync(memoryId, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (record is null)
            return false;

        record.IsDeleted = true;

        await factCollection.UpsertAsync(record, cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <inheritdoc />
    public async ValueTask<bool> HardDeleteMemoryAsync(
        string memoryId,
        CancellationToken cancellationToken = default)
    {
        var record = await factCollection.GetAsync(memoryId, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (record is null)
            return false;

        await factCollection.DeleteAsync(memoryId, cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <inheritdoc />
    public async ValueTask<bool> RestoreMemoryAsync(
        string memoryId,
        CancellationToken cancellationToken = default)
    {
        var record = await factCollection.GetAsync(memoryId, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (record is null)
            return false;

        record.IsDeleted = false;

        await factCollection.UpsertAsync(record, cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <inheritdoc />
    public async ValueTask<int> GetMemoryCountAsync(
        string entityId,
        bool includeDeleted = false,
        CancellationToken cancellationToken = default)
    {
        var count = 0;
        Expression<Func<MemoryFactRecord, bool>> filter = includeDeleted
            ? r => r.EntityId == entityId
            : r => r.EntityId == entityId && !r.IsDeleted;

        await foreach (var _ in factCollection.GetAsync(
            filter,
            int.MaxValue,
            cancellationToken: cancellationToken).ConfigureAwait(false))
        {
            count++;
        }

        return count;
    }

    static Expression<Func<MemoryFactRecord, bool>> BuildSearchFilter(
        string entityId,
        string? memoryType,
        string? scope,
        bool includeDeleted)
    {
        if (includeDeleted && memoryType is null && scope is null)
            return r => r.EntityId == entityId;

        if (!includeDeleted && memoryType is null && scope is null)
            return r => r.EntityId == entityId && !r.IsDeleted;

        if (includeDeleted && memoryType is not null && scope is null)
            return r => r.EntityId == entityId && r.MemoryType == memoryType;

        if (!includeDeleted && memoryType is not null && scope is null)
            return r => r.EntityId == entityId && r.MemoryType == memoryType && !r.IsDeleted;

        if (includeDeleted && memoryType is null && scope is not null)
            return r => r.EntityId == entityId && r.Scope == scope;

        if (!includeDeleted && memoryType is null && scope is not null)
            return r => r.EntityId == entityId && r.Scope == scope && !r.IsDeleted;

        if (includeDeleted && memoryType is not null && scope is not null)
            return r => r.EntityId == entityId && r.MemoryType == memoryType && r.Scope == scope;

        return r => r.EntityId == entityId && r.MemoryType == memoryType && r.Scope == scope && !r.IsDeleted;
    }
}
