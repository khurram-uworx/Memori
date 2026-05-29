using Microsoft.Extensions.VectorData;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Numerics.Tensors;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Memori.Storage;

file static class VectorStoreSchema
{
    public static PropertyInfo GetKeyProperty<TRecord>()
    {
        var type = typeof(TRecord);
        foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (prop.GetCustomAttribute<VectorStoreKeyAttribute>() is not null)
                return prop;
        }

        throw new InvalidOperationException(
            $"Type '{type.FullName}' does not have a property marked with [VectorStoreKey].");
    }

    public static PropertyInfo? GetVectorProperty<TRecord>()
    {
        var type = typeof(TRecord);
        foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (prop.GetCustomAttribute<VectorStoreVectorAttribute>() is not null)
                return prop;
        }

        return null;
    }

    public static IReadOnlyList<PropertyInfo> GetTextSearchableProperties<TRecord>()
    {
        var type = typeof(TRecord);
        var result = new List<PropertyInfo>();

        foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var attr = prop.GetCustomAttribute<VectorStoreDataAttribute>();
            if (attr is not null && attr.IsFullTextIndexed && prop.PropertyType == typeof(string))
                result.Add(prop);
        }

        if (result.Count == 0)
            foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                if (prop.PropertyType == typeof(string))
                    result.Add(prop);

        return result;
    }

    public static IReadOnlyList<PropertyInfo> GetDataProperties<TRecord>()
    {
        var type = typeof(TRecord);
        var result = new List<PropertyInfo>();

        foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            if (prop.GetCustomAttribute<VectorStoreDataAttribute>() is not null)
                result.Add(prop);

        return result;
    }
}

/// <summary>
/// Thread-safe in-memory implementation of <see cref="VectorStore"/>.
/// </summary>
/// <remarks>
/// This implementation is intended for tests, demos, local development, and as
/// reference behavior for custom vector store providers. It is not durable across
/// process restarts.
/// </remarks>
public sealed class InMemoriVectorStore : VectorStore
{
    readonly ConcurrentDictionary<string, object> collections = new(StringComparer.Ordinal);
    readonly object gate = new();

    /// <inheritdoc />
    [RequiresDynamicCode("This API requires dynamic code generation for schema discovery.")]
    [RequiresUnreferencedCode("This API uses reflection to discover VectorStore attributes on TRecord.")]
    public override VectorStoreCollection<TKey, TRecord> GetCollection<TKey, TRecord>(
        string name,
        VectorStoreCollectionDefinition? definition = null)
    {
        ArgumentNullException.ThrowIfNull(name);

        object? collection;
        lock (gate)
        {
            if (collections.TryGetValue(name, out collection))
            {
                if (collection is InMemoryVectorStoreCollection<TKey, TRecord> typed)
                    return typed;

                throw new VectorStoreException(
                    $"Collection '{name}' already exists with a different record type.");
            }

            var newCollection = new InMemoryVectorStoreCollection<TKey, TRecord>(name);
            collections[name] = newCollection;
            return newCollection;
        }
    }

    /// <inheritdoc />
    public override VectorStoreCollection<object, Dictionary<string, object?>> GetDynamicCollection(
        string name,
        VectorStoreCollectionDefinition definition)
    {
        throw new NotSupportedException(
            "Dynamic collections are not supported. Use the typed GetCollection<TKey, TRecord>() instead.");
    }

    /// <inheritdoc />
    public override Task<bool> CollectionExistsAsync(string name, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(name);

        lock (gate)
        {
            return Task.FromResult(collections.ContainsKey(name));
        }
    }

    /// <inheritdoc />
    public override Task EnsureCollectionDeletedAsync(string name, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(name);

        lock (gate)
        {
            if (collections.TryRemove(name, out var existing))
                if (existing is IDisposable disposable)
                    disposable.Dispose();
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public override async IAsyncEnumerable<string> ListCollectionNamesAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        foreach (var name in collections.Keys)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return name;
        }

        await Task.CompletedTask.ConfigureAwait(false);
    }

    /// <inheritdoc />
    public override object? GetService(Type serviceType, object? serviceKey = null)
    {
        ArgumentNullException.ThrowIfNull(serviceType);

        if (serviceType == typeof(InMemoriVectorStore) || serviceType == typeof(VectorStore))
            return this;

        return null;
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            lock (gate)
            {
                foreach (var value in collections.Values)
                    if (value is IDisposable disposable)
                        disposable.Dispose();

                collections.Clear();
            }
        }
    }
}

file sealed class InMemoryVectorStoreCollection<TKey, TRecord> : VectorStoreCollection<TKey, TRecord>
    where TKey : notnull
    where TRecord : class
{
    readonly ConcurrentDictionary<TKey, TRecord> records = new();
    readonly string name;
    volatile bool deleted;

    static readonly PropertyInfo keyProperty = VectorStoreSchema.GetKeyProperty<TRecord>();
    static readonly PropertyInfo? vectorProperty = VectorStoreSchema.GetVectorProperty<TRecord>();
    static readonly IReadOnlyList<PropertyInfo> textProperties = VectorStoreSchema.GetTextSearchableProperties<TRecord>();

    public InMemoryVectorStoreCollection(string name)
    {
        this.name = name ?? throw new ArgumentNullException(nameof(name));
    }

    /// <inheritdoc />
    public override string Name => name;

    void checkNotDeleted()
    {
        if (deleted)
            throw new VectorStoreException($"Collection '{name}' has been deleted.")
            {
                CollectionName = name,
                OperationName = "write"
            };
    }

    static TKey extractKey(TRecord record)
    {
        var value = keyProperty.GetValue(record);
        return (TKey)value!;
    }

    /// <inheritdoc />
    public override Task UpsertAsync(TRecord record, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(record);
        checkNotDeleted();

        var key = extractKey(record);
        records[key] = record;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public override Task UpsertAsync(IEnumerable<TRecord> records, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(records);
        checkNotDeleted();

        foreach (var record in records)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var key = extractKey(record);
            this.records[key] = record;
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public override Task<TRecord?> GetAsync(TKey key, RecordRetrievalOptions? options = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (records.TryGetValue(key, out var record))
            return Task.FromResult<TRecord?>(record);

        return Task.FromResult<TRecord?>(null);
    }

    /// <inheritdoc />
    public override async IAsyncEnumerable<TRecord> GetAsync(
        IEnumerable<TKey> keys,
        RecordRetrievalOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(keys);

        foreach (var key in keys)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (records.TryGetValue(key, out var record))
                yield return record;
        }

        await Task.CompletedTask.ConfigureAwait(false);
    }

    /// <inheritdoc />
    public override async IAsyncEnumerable<TRecord> GetAsync(
        Expression<Func<TRecord, bool>> filter,
        int top,
        FilteredRecordRetrievalOptions<TRecord>? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(filter);

        if (top <= 0)
            yield break;

        var filterFunc = filter.Compile();
        var skip = options?.Skip ?? 0;
        var count = 0;

        foreach (var (key, record) in records)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!filterFunc(record))
                continue;

            if (skip > 0)
            {
                skip--;
                continue;
            }

            yield return record;
            count++;

            if (count >= top)
                yield break;
        }

        await Task.CompletedTask.ConfigureAwait(false);
    }

    /// <inheritdoc />
    public override Task DeleteAsync(TKey key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        records.TryRemove(key, out _);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public override Task DeleteAsync(IEnumerable<TKey> keys, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(keys);

        foreach (var key in keys)
        {
            cancellationToken.ThrowIfCancellationRequested();
            records.TryRemove(key, out _);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public override async IAsyncEnumerable<VectorSearchResult<TRecord>> SearchAsync<TInput>(
        TInput searchValue,
        int top,
        VectorSearchOptions<TRecord>? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (top <= 0)
            yield break;

        var filterFunc = options?.Filter?.Compile();
        var scoreThreshold = options?.ScoreThreshold;
        var skip = options?.Skip ?? 0;

        if (searchValue is ReadOnlyMemory<float> queryVector)
        {
            if (vectorProperty is null)
                yield break;

            var scored = new List<(TRecord Record, double Score)>();

            foreach (var (key, record) in records)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (filterFunc is not null && !filterFunc(record))
                    continue;

                var vectorValue = vectorProperty.GetValue(record);
                if (vectorValue is not ReadOnlyMemory<float> recordVector)
                    continue;

                if (recordVector.Length == 0 || queryVector.Length == 0)
                    continue;

                double score;
                try
                {
                    score = TensorPrimitives.CosineSimilarity(queryVector.Span, recordVector.Span);
                }
                catch
                {
                    continue;
                }

                if (double.IsNaN(score) || (scoreThreshold.HasValue && score < scoreThreshold.Value))
                    continue;

                scored.Add((record, score));
            }

            scored.Sort((a, b) => b.Score.CompareTo(a.Score));

            var returned = 0;
            foreach (var (record, score) in scored)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (skip > 0)
                {
                    skip--;
                    continue;
                }

                yield return new VectorSearchResult<TRecord>(record, score);
                returned++;

                if (returned >= top)
                    yield break;
            }
        }
        else if (searchValue is string textQuery)
        {
            var queryTerms = Tokenize(textQuery);
            if (queryTerms.Count == 0)
                yield break;

            if (textProperties.Count == 0)
                yield break;

            var scored = new List<(TRecord Record, double Score)>();

            foreach (var (key, record) in records)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (filterFunc is not null && !filterFunc(record))
                    continue;

                double bestScore = 0;

                foreach (var textProp in textProperties)
                {
                    var textValue = textProp.GetValue(record) as string;
                    if (string.IsNullOrEmpty(textValue))
                        continue;

                    var contentTerms = Tokenize(textValue);
                    if (contentTerms.Count == 0)
                        continue;

                    var overlap = queryTerms.Count(t => contentTerms.Contains(t));
                    var lexicalScore = (double)overlap / queryTerms.Count;

                    if (lexicalScore > bestScore)
                        bestScore = lexicalScore;
                }

                if (bestScore <= 0 || (scoreThreshold.HasValue && bestScore < scoreThreshold.Value))
                    continue;

                scored.Add((record, bestScore));
            }

            scored.Sort((a, b) => b.Score.CompareTo(a.Score));

            var returned = 0;
            foreach (var (record, score) in scored)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (skip > 0)
                {
                    skip--;
                    continue;
                }

                yield return new VectorSearchResult<TRecord>(record, score);
                returned++;

                if (returned >= top)
                    yield break;
            }
        }
        else
        {
            throw new VectorStoreException(
                $"Unsupported search input type '{typeof(TInput).FullName}'. " +
                "Supported types are string (text search) and ReadOnlyMemory<float> (vector search).");
        }

        await Task.CompletedTask.ConfigureAwait(false);
    }

    /// <inheritdoc />
    public override Task<bool> CollectionExistsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(!deleted);
    }

    /// <inheritdoc />
    public override Task EnsureCollectionExistsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public override Task EnsureCollectionDeletedAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        records.Clear();
        deleted = true;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public override object? GetService(Type serviceType, object? serviceKey = null)
    {
        ArgumentNullException.ThrowIfNull(serviceType);

        if (serviceType == typeof(InMemoryVectorStoreCollection<TKey, TRecord>))
            return this;

        if (serviceType == typeof(VectorStoreCollection<TKey, TRecord>))
            return this;

        if (serviceType == typeof(IVectorSearchable<TRecord>))
            return this;

        return null;
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
            records.Clear();
    }

    static HashSet<string> Tokenize(string text)
    {
        var terms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var start = -1;

        for (var i = 0; i <= text.Length; i++)
        {
            if (i < text.Length && char.IsLetterOrDigit(text[i]))
            {
                if (start < 0)
                    start = i;

                continue;
            }

            if (start < 0)
                continue;

            var token = text[start..i];

            if (token.Length > 1)
                terms.Add(token);

            start = -1;
        }

        return terms;
    }
}
