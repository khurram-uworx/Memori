using Memori.Models;
using Microsoft.Extensions.VectorData;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;

namespace Memori.Search;

/// <summary>
/// Composite vector store collection that queries multiple <see cref="VectorStoreCollection{TKey, TRecord}"/> backends in parallel
/// and merges results using a distributed ranker.
/// </summary>
/// <remarks>
/// This collection is transparent to consumers like <see cref="MemorySearchService"/> — it acts like a single collection
/// while internally querying multiple backends and merging results.
/// Failures in one backend do not crash others; partial failures are handled gracefully.
/// </remarks>
public sealed class CompositeMemoryCollection : VectorStoreCollection<string, MemoryFactRecord>
{
    readonly IReadOnlyList<VectorStoreCollection<string, MemoryFactRecord>> backends;
    readonly IDistributedRanker ranker;
    readonly CompositeMemoryCollectionOptions options;

    /// <summary>
    /// Creates a composite memory collection.
    /// </summary>
    /// <param name="backends">The backend collections to compose. Must contain at least one backend.</param>
    /// <param name="options">Configuration options. If null, uses defaults.</param>
    /// <param name="ranker">Optional distributed ranker for merging results. Created from options if not provided.</param>
    public CompositeMemoryCollection(
        IReadOnlyList<VectorStoreCollection<string, MemoryFactRecord>> backends,
        CompositeMemoryCollectionOptions? options = null,
        IDistributedRanker? ranker = null)
    {
        if (backends is null || backends.Count == 0)
            throw new ArgumentException("At least one backend is required.", nameof(backends));

        this.backends = backends;
        this.options = options ?? new CompositeMemoryCollectionOptions();
        this.ranker = ranker ?? new DefaultDistributedRanker(this.options.RankingStrategy, this.options.SourceWeights);
    }

    /// <inheritdoc />
    public override string Name => options.Name;

    /// <inheritdoc />
    public override async Task UpsertAsync(MemoryFactRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);

        if (options.WriteStrategy == CompositeWriteStrategy.PrimaryOnly)
        {
            await backends[0].UpsertAsync(record, cancellationToken).ConfigureAwait(false);
            return;
        }

        var tasks = backends.Select(backend => UpsertSafelyAsync(backend, record, cancellationToken));
        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public override async Task UpsertAsync(IEnumerable<MemoryFactRecord> records, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(records);

        var recordList = records.ToList();

        if (options.WriteStrategy == CompositeWriteStrategy.PrimaryOnly)
        {
            await backends[0].UpsertAsync(recordList, cancellationToken).ConfigureAwait(false);
            return;
        }

        var tasks = backends.Select(backend => UpsertBatchSafelyAsync(backend, recordList, cancellationToken));
        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public override async Task<MemoryFactRecord?> GetAsync(string key, RecordRetrievalOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);

        foreach (var backend in backends)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await backend.GetAsync(key, options, cancellationToken).ConfigureAwait(false);
            if (result is not null)
                return result;
        }

        return null;
    }

    /// <inheritdoc />
    public override async IAsyncEnumerable<MemoryFactRecord> GetAsync(
        IEnumerable<string> keys,
        RecordRetrievalOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(keys);

        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var key in keys)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var record = await GetAsync(key, options, cancellationToken).ConfigureAwait(false);
            if (record is not null && seen.Add(key))
                yield return record;
        }

        await Task.CompletedTask.ConfigureAwait(false);
    }

    /// <inheritdoc />
    public override async IAsyncEnumerable<MemoryFactRecord> GetAsync(
        Expression<Func<MemoryFactRecord, bool>> filter,
        int top,
        FilteredRecordRetrievalOptions<MemoryFactRecord>? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        if (top <= 0)
            yield break;

        var allTasks = backends.Select(backend =>
            backend.GetAsync(filter, top, options, cancellationToken).ToEnumerableAsync(cancellationToken));

        var results = await Task.WhenAll(allTasks).ConfigureAwait(false);
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var backendResults in results)
        {
            foreach (var record in backendResults)
            {
                if (seen.Add(record.Id))
                    yield return record;
            }
        }

        await Task.CompletedTask.ConfigureAwait(false);
    }

    /// <inheritdoc />
    public override async Task DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        if (options.WriteStrategy == CompositeWriteStrategy.PrimaryOnly)
        {
            await backends[0].DeleteAsync(key, cancellationToken).ConfigureAwait(false);
            return;
        }

        var tasks = backends.Select(backend => DeleteSafelyAsync(backend, key, cancellationToken));
        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public override async Task DeleteAsync(IEnumerable<string> keys, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(keys);

        var keyList = keys.ToList();

        if (options.WriteStrategy == CompositeWriteStrategy.PrimaryOnly)
        {
            await backends[0].DeleteAsync(keyList, cancellationToken).ConfigureAwait(false);
            return;
        }

        var tasks = backends.Select(backend => DeleteBatchSafelyAsync(backend, keyList, cancellationToken));
        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public override async IAsyncEnumerable<VectorSearchResult<MemoryFactRecord>> SearchAsync<TInput>(
        TInput searchValue,
        int top,
        VectorSearchOptions<MemoryFactRecord>? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (top <= 0)
            yield break;

        var semaphore = new SemaphoreSlim(this.options.MaxConcurrency);
        var searchTasks = backends.Select(async (backend, index) =>
        {
            await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var results = new List<VectorSearchResult<MemoryFactRecord>>();
                await foreach (var result in backend.SearchAsync(searchValue, top, options, cancellationToken)
                    .ConfigureAwait(false))
                {
                    results.Add(result);
                }
                return (BackendIndex: index, BackendName: backend.Name, Results: results);
            }
            catch
            {
                return (BackendIndex: index, BackendName: backend.Name, Results: (List<VectorSearchResult<MemoryFactRecord>>?)null);
            }
            finally
            {
                semaphore.Release();
            }
        });

        var completed = await Task.WhenAll(searchTasks).ConfigureAwait(false);

        var sourceResults = new List<IReadOnlyList<RecallResult>>();
        var sourceNames = new List<string>();

        for (int i = 0; i < completed.Length; i++)
        {
            var backendResult = completed[i];
            if (backendResult.Results is null)
                continue;

            sourceNames.Add(backendResult.BackendName);

            var recallResults = backendResult.Results
                .Select(r => new RecallResult(
                    factId: r.Record.Id,
                    content: r.Record.Content,
                    similarity: r.Score ?? 0,
                    rankScore: r.Score ?? 0,
                    createdAt: r.Record.CreatedAt,
                    summaries: r.Record.Summaries,
                    confidence: r.Record.Confidence,
                    memoryType: r.Record.MemoryType))
                .ToList();

            sourceResults.Add(recallResults);
        }

        var ranked = ranker.Rank(sourceResults, DateTimeOffset.UtcNow);

        var finalResults = ranked.Take(top).ToList();

        foreach (var result in finalResults)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var backendIndex = FindBackendIndexForFact(result.FactId, sourceNames);
            yield return CreateSearchResult(backendIndex, result);
        }

        await Task.CompletedTask.ConfigureAwait(false);
    }

    /// <inheritdoc />
    public override async Task<bool> CollectionExistsAsync(CancellationToken cancellationToken = default)
    {
        foreach (var backend in backends)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await backend.CollectionExistsAsync(cancellationToken).ConfigureAwait(false))
                return true;
        }
        return false;
    }

    /// <inheritdoc />
    public override async Task EnsureCollectionExistsAsync(CancellationToken cancellationToken = default)
    {
        foreach (var backend in backends)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await EnsureExistsSafelyAsync(backend, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public override async Task EnsureCollectionDeletedAsync(CancellationToken cancellationToken = default)
    {
        var tasks = backends.Select(backend => EnsureDeletedSafelyAsync(backend, cancellationToken));
        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public override object? GetService(Type serviceType, object? serviceKey = null)
    {
        ArgumentNullException.ThrowIfNull(serviceType);

        if (serviceType == typeof(CompositeMemoryCollection))
            return this;

        if (serviceType == typeof(VectorStoreCollection<string, MemoryFactRecord>))
            return this;

        foreach (var backend in backends)
        {
            var service = backend.GetService(serviceType, serviceKey);
            if (service is not null)
                return service;
        }

        return null;
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            foreach (var backend in backends)
            {
                if (backend is IDisposable disposable)
                    disposable.Dispose();
            }
        }
    }

    async Task UpsertSafelyAsync(VectorStoreCollection<string, MemoryFactRecord> backend, MemoryFactRecord record, CancellationToken ct)
    {
        try
        {
            await backend.UpsertAsync(record, ct).ConfigureAwait(false);
        }
        catch
        {
        }
    }

    async Task UpsertBatchSafelyAsync(VectorStoreCollection<string, MemoryFactRecord> backend, IReadOnlyList<MemoryFactRecord> records, CancellationToken ct)
    {
        try
        {
            await backend.UpsertAsync(records, ct).ConfigureAwait(false);
        }
        catch
        {
        }
    }

    async Task DeleteSafelyAsync(VectorStoreCollection<string, MemoryFactRecord> backend, string key, CancellationToken ct)
    {
        try
        {
            await backend.DeleteAsync(key, ct).ConfigureAwait(false);
        }
        catch
        {
        }
    }

    async Task DeleteBatchSafelyAsync(VectorStoreCollection<string, MemoryFactRecord> backend, IReadOnlyList<string> keys, CancellationToken ct)
    {
        try
        {
            await backend.DeleteAsync(keys, ct).ConfigureAwait(false);
        }
        catch
        {
        }
    }

    async Task EnsureExistsSafelyAsync(VectorStoreCollection<string, MemoryFactRecord> backend, CancellationToken ct)
    {
        try
        {
            await backend.EnsureCollectionExistsAsync(ct).ConfigureAwait(false);
        }
        catch
        {
        }
    }

    async Task EnsureDeletedSafelyAsync(VectorStoreCollection<string, MemoryFactRecord> backend, CancellationToken ct)
    {
        try
        {
            await backend.EnsureCollectionDeletedAsync(ct).ConfigureAwait(false);
        }
        catch
        {
        }
    }

    int FindBackendIndexForFact(string factId, IReadOnlyList<string> sourceNames)
    {
        for (int i = 0; i < sourceNames.Count; i++)
        {
            var backend = backends[i];
            if (backend.Name == sourceNames[i])
                return i;
        }
        return 0;
    }

    VectorSearchResult<MemoryFactRecord> CreateSearchResult(int backendIndex, RecallResult recallResult)
    {
        var record = new MemoryFactRecord(
            id: recallResult.FactId,
            entityId: string.Empty,
            content: recallResult.Content,
            embedding: ReadOnlyMemory<float>.Empty,
            memoryType: recallResult.MemoryType,
            confidence: recallResult.Confidence,
            createdAt: recallResult.CreatedAt);

        return new VectorSearchResult<MemoryFactRecord>(record, recallResult.Similarity);
    }
}

file static class AsyncEnumerableExtensions
{
    public static async Task<List<T>> ToEnumerableAsync<T>(this IAsyncEnumerable<T> source, CancellationToken cancellationToken)
    {
        var list = new List<T>();
        await foreach (var item in source.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            list.Add(item);
        }
        return list;
    }
}
