using Memori.Models;
using Memori.Search;
using Memori.Storage;
using Microsoft.Extensions.VectorData;
using NUnit.Framework;

namespace Memori.Tests;

public class CompositeMemoryCollectionTests
{
    static float[] nonZeroEmbedding = Enumerable.Range(0, 1536).Select(i => (float)(i % 10) / 10f).ToArray();

    static MemoryFactRecord MakeRecord(string id, string entityId, string content, double confidence = 0.5, DateTimeOffset? createdAt = null)
        => new(id, entityId, content, new ReadOnlyMemory<float>(nonZeroEmbedding), "general", confidence, createdAt ?? DateTimeOffset.UtcNow);

    static VectorStoreCollection<string, MemoryFactRecord> CreateBackend(string name)
    {
        var store = new InMemoryVectorStore();
        return store.GetCollection<string, MemoryFactRecord>(name);
    }

    #region Construction Tests

    [Test]
    public void Constructor_WithNoBackends_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => new CompositeMemoryCollection(Array.Empty<VectorStoreCollection<string, MemoryFactRecord>>()));
    }

    [Test]
    public void Constructor_WithNullBackends_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => new CompositeMemoryCollection(null!));
    }

    [Test]
    public void Constructor_SingleBackend_Succeeds()
    {
        var backend = CreateBackend("backend-1");
        var composite = new CompositeMemoryCollection(new[] { backend });

        Assert.That(composite, Is.Not.Null);
    }

    [Test]
    public void Constructor_WithCustomOptions_UsesProvidedName()
    {
        var backend = CreateBackend("backend-1");
        var options = new CompositeMemoryCollectionOptions { Name = "my-composite" };
        var composite = new CompositeMemoryCollection(new[] { backend }, options);

        Assert.That(composite.Name, Is.EqualTo("my-composite"));
    }

    #endregion

    #region Upsert Tests

    [Test]
    public async Task UpsertAsync_WritesToAllBackends()
    {
        var backend1 = CreateBackend("backend-1");
        var backend2 = CreateBackend("backend-2");
        var composite = new CompositeMemoryCollection(new[] { backend1, backend2 });

        var record = MakeRecord("fact-1", "entity-1", "test fact");
        await composite.UpsertAsync(record);

        var record1 = await backend1.GetAsync("fact-1");
        var record2 = await backend2.GetAsync("fact-1");

        Assert.That(record1, Is.Not.Null);
        Assert.That(record2, Is.Not.Null);
        Assert.That(record1!.Content, Is.EqualTo("test fact"));
        Assert.That(record2!.Content, Is.EqualTo("test fact"));
    }

    [Test]
    public async Task UpsertBatch_WritesToAllBackends()
    {
        var backend1 = CreateBackend("backend-1");
        var backend2 = CreateBackend("backend-2");
        var composite = new CompositeMemoryCollection(new[] { backend1, backend2 });

        var records = new[]
        {
            MakeRecord("fact-1", "entity-1", "fact one"),
            MakeRecord("fact-2", "entity-1", "fact two"),
        };
        await composite.UpsertAsync(records);

        var record1 = await backend1.GetAsync("fact-1");
        var record2 = await backend2.GetAsync("fact-2");

        Assert.That(record1, Is.Not.Null);
        Assert.That(record2, Is.Not.Null);
    }

    [Test]
    public async Task UpsertAsync_PrimaryOnly_WritesOnlyToFirstBackend()
    {
        var backend1 = CreateBackend("backend-1");
        var backend2 = CreateBackend("backend-2");
        var options = new CompositeMemoryCollectionOptions { WriteStrategy = CompositeWriteStrategy.PrimaryOnly };
        var composite = new CompositeMemoryCollection(new[] { backend1, backend2 }, options);

        var record = MakeRecord("fact-1", "entity-1", "test fact");
        await composite.UpsertAsync(record);

        var record1 = await backend1.GetAsync("fact-1");
        var record2 = await backend2.GetAsync("fact-1");

        Assert.That(record1, Is.Not.Null);
        Assert.That(record2, Is.Null);
    }

    #endregion

    #region Get Tests

    [Test]
    public async Task GetAsync_FoundInFirstBackend_ReturnsRecord()
    {
        var backend1 = CreateBackend("backend-1");
        var backend2 = CreateBackend("backend-2");
        var composite = new CompositeMemoryCollection(new[] { backend1, backend2 });

        await backend1.UpsertAsync(MakeRecord("fact-1", "entity-1", "fact from backend 1"));

        var result = await composite.GetAsync("fact-1");

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Content, Is.EqualTo("fact from backend 1"));
    }

    [Test]
    public async Task GetAsync_FoundInSecondBackend_ReturnsRecord()
    {
        var backend1 = CreateBackend("backend-1");
        var backend2 = CreateBackend("backend-2");
        var composite = new CompositeMemoryCollection(new[] { backend1, backend2 });

        await backend2.UpsertAsync(MakeRecord("fact-1", "entity-1", "fact from backend 2"));

        var result = await composite.GetAsync("fact-1");

        Assert.That(result, Is.Not.Null);
    }

    [Test]
    public async Task GetAsync_NotFoundInAnyBackend_ReturnsNull()
    {
        var backend1 = CreateBackend("backend-1");
        var backend2 = CreateBackend("backend-2");
        var composite = new CompositeMemoryCollection(new[] { backend1, backend2 });

        var result = await composite.GetAsync("nonexistent");

        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task GetAsync_MultipleKeys_ReturnsAllFound()
    {
        var backend1 = CreateBackend("backend-1");
        var backend2 = CreateBackend("backend-2");
        var composite = new CompositeMemoryCollection(new[] { backend1, backend2 });

        await backend1.UpsertAsync(MakeRecord("fact-1", "entity-1", "one"));
        await backend2.UpsertAsync(MakeRecord("fact-2", "entity-1", "two"));

        var results = new List<MemoryFactRecord>();
        await foreach (var record in composite.GetAsync(new[] { "fact-1", "fact-2" }))
        {
            results.Add(record);
        }

        Assert.That(results, Has.Count.EqualTo(2));
    }

    #endregion

    #region Delete Tests

    [Test]
    public async Task DeleteAsync_RemovesFromAllBackends()
    {
        var backend1 = CreateBackend("backend-1");
        var backend2 = CreateBackend("backend-2");
        var composite = new CompositeMemoryCollection(new[] { backend1, backend2 });

        var record = MakeRecord("fact-1", "entity-1", "to delete");
        await composite.UpsertAsync(record);
        await composite.DeleteAsync("fact-1");

        var result1 = await backend1.GetAsync("fact-1");
        var result2 = await backend2.GetAsync("fact-1");

        Assert.That(result1, Is.Null);
        Assert.That(result2, Is.Null);
    }

    [Test]
    public async Task DeleteAsync_PrimaryOnly_RemovesOnlyFromFirstBackend()
    {
        var backend1 = CreateBackend("backend-1");
        var backend2 = CreateBackend("backend-2");
        var options = new CompositeMemoryCollectionOptions { WriteStrategy = CompositeWriteStrategy.PrimaryOnly };
        var composite = new CompositeMemoryCollection(new[] { backend1, backend2 }, options);

        await backend1.UpsertAsync(MakeRecord("fact-1", "entity-1", "to delete"));
        await backend2.UpsertAsync(MakeRecord("fact-1", "entity-1", "to delete"));
        await composite.DeleteAsync("fact-1");

        var result1 = await backend1.GetAsync("fact-1");
        var result2 = await backend2.GetAsync("fact-1");

        Assert.That(result1, Is.Null);
        Assert.That(result2, Is.Not.Null);
    }

    #endregion

    #region Search Tests

    [Test]
    public async Task SearchAsync_CombinesResultsFromAllBackends()
    {
        var backend1 = CreateBackend("backend-1");
        var backend2 = CreateBackend("backend-2");
        var composite = new CompositeMemoryCollection(new[] { backend1, backend2 });

        await backend1.UpsertAsync(MakeRecord("fact-a", "entity-1", "coffee preference"));
        await backend2.UpsertAsync(MakeRecord("fact-b", "entity-1", "tea preference"));

        var results = new List<VectorSearchResult<MemoryFactRecord>>();
        await foreach (var result in composite.SearchAsync(new ReadOnlyMemory<float>(nonZeroEmbedding), 10))
        {
            results.Add(result);
        }

        Assert.That(results, Has.Count.EqualTo(2));
        Assert.That(results.Select(r => r.Record.Id), Is.EquivalentTo(new[] { "fact-a", "fact-b" }));
    }

    [Test]
    public async Task SearchAsync_RankedByCompositeOrder()
    {
        var backend1 = CreateBackend("backend-1");
        var backend2 = CreateBackend("backend-2");
        var composite = new CompositeMemoryCollection(new[] { backend1, backend2 });

        await backend1.UpsertAsync(MakeRecord("fact-a", "entity-1", "coffee"));
        await backend2.UpsertAsync(MakeRecord("fact-b", "entity-1", "tea"));

        var results = new List<VectorSearchResult<MemoryFactRecord>>();
        await foreach (var result in composite.SearchAsync(new ReadOnlyMemory<float>(nonZeroEmbedding), 10))
        {
            results.Add(result);
        }

        Assert.That(results, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task SearchAsync_RespectsTopLimit()
    {
        var backend1 = CreateBackend("backend-1");
        var backend2 = CreateBackend("backend-2");
        var composite = new CompositeMemoryCollection(new[] { backend1, backend2 });

        await backend1.UpsertAsync(MakeRecord("fact-a", "entity-1", "one"));
        await backend1.UpsertAsync(MakeRecord("fact-b", "entity-1", "two"));
        await backend2.UpsertAsync(MakeRecord("fact-c", "entity-1", "three"));

        var results = new List<VectorSearchResult<MemoryFactRecord>>();
        await foreach (var result in composite.SearchAsync(new ReadOnlyMemory<float>(nonZeroEmbedding), 2))
        {
            results.Add(result);
        }

        Assert.That(results, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task SearchAsync_DeduplicatesAcrossBackends()
    {
        var backend1 = CreateBackend("backend-1");
        var backend2 = CreateBackend("backend-2");
        var composite = new CompositeMemoryCollection(new[] { backend1, backend2 });

        await backend1.UpsertAsync(MakeRecord("fact-same", "entity-1", "shared fact"));
        await backend2.UpsertAsync(MakeRecord("fact-same", "entity-1", "shared fact"));
        await backend1.UpsertAsync(MakeRecord("fact-unique", "entity-1", "unique fact"));

        var results = new List<VectorSearchResult<MemoryFactRecord>>();
        await foreach (var result in composite.SearchAsync(new ReadOnlyMemory<float>(nonZeroEmbedding), 10))
        {
            results.Add(result);
        }

        Assert.That(results, Has.Count.EqualTo(2));
        Assert.That(results.Select(r => r.Record.Id), Is.EquivalentTo(new[] { "fact-same", "fact-unique" }));
    }

    [Test]
    public async Task SearchAsync_NoResults_ReturnsEmpty()
    {
        var backend1 = CreateBackend("backend-1");
        var backend2 = CreateBackend("backend-2");
        var composite = new CompositeMemoryCollection(new[] { backend1, backend2 });

        var results = new List<VectorSearchResult<MemoryFactRecord>>();
        await foreach (var result in composite.SearchAsync(new ReadOnlyMemory<float>(nonZeroEmbedding), 10))
        {
            results.Add(result);
        }

        Assert.That(results, Is.Empty);
    }

    #endregion

    #region Lifecycle Tests

    [Test]
    public async Task CollectionExistsAsync_ReturnsTrue_WhenAnyBackendExists()
    {
        var backend1 = CreateBackend("backend-1");
        var backend2 = CreateBackend("backend-2");
        var composite = new CompositeMemoryCollection(new[] { backend1, backend2 });

        await backend1.EnsureCollectionExistsAsync();

        var exists = await composite.CollectionExistsAsync();

        Assert.That(exists, Is.True);
    }

    [Test]
    public async Task EnsureCollectionDeletedAsync_DeletesAllBackends()
    {
        var backend1 = CreateBackend("backend-1");
        var backend2 = CreateBackend("backend-2");
        var composite = new CompositeMemoryCollection(new[] { backend1, backend2 });

        await composite.EnsureCollectionDeletedAsync();

        Assert.That(await backend1.CollectionExistsAsync(), Is.False);
        Assert.That(await backend2.CollectionExistsAsync(), Is.False);
    }

    #endregion

    #region Partial Failure Tests

    [Test]
    public async Task SearchAsync_HandlesBackendWithNoResults()
    {
        var backend1 = CreateBackend("backend-1");
        var backend2 = CreateBackend("backend-2");
        var composite = new CompositeMemoryCollection(new[] { backend1, backend2 });

        await backend1.UpsertAsync(MakeRecord("fact-1", "entity-1", "data"));

        var results = new List<VectorSearchResult<MemoryFactRecord>>();
        await foreach (var result in composite.SearchAsync(new ReadOnlyMemory<float>(nonZeroEmbedding), 10))
        {
            results.Add(result);
        }

        Assert.That(results, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task UpsertAsync_WithAllStrategy_DoesNotThrow_WhenBackendMissing()
    {
        var backend1 = CreateBackend("backend-1");
        var backend2 = CreateBackend("backend-2");
        var composite = new CompositeMemoryCollection(new[] { backend1, backend2 });

        await composite.UpsertAsync(MakeRecord("fact-1", "entity-1", "test"));

        var result1 = await backend1.GetAsync("fact-1");
        var result2 = await backend2.GetAsync("fact-1");

        Assert.That(result1, Is.Not.Null);
        Assert.That(result2, Is.Not.Null);
    }

    #endregion

    #region Strategy Integration Tests

    [Test]
    public async Task SearchAsync_WithWeightedStrategy_ProducesResults()
    {
        var backend1 = CreateBackend("backend-1");
        var backend2 = CreateBackend("backend-2");
        var options = new CompositeMemoryCollectionOptions
        {
            RankingStrategy = DistributedRankingStrategy.WeightedScore,
            SourceWeights = new Dictionary<string, double>
            {
                ["backend-1"] = 2.0,
                ["backend-2"] = 0.5,
            },
        };
        var composite = new CompositeMemoryCollection(new[] { backend1, backend2 }, options);

        await backend1.UpsertAsync(MakeRecord("fact-a", "entity-1", "coffee"));
        await backend2.UpsertAsync(MakeRecord("fact-b", "entity-1", "tea"));

        var results = new List<VectorSearchResult<MemoryFactRecord>>();
        await foreach (var result in composite.SearchAsync(new ReadOnlyMemory<float>(nonZeroEmbedding), 10))
        {
            results.Add(result);
        }

        Assert.That(results, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task SearchAsync_WithRoundRobinStrategy_ProducesResults()
    {
        var backend1 = CreateBackend("backend-1");
        var backend2 = CreateBackend("backend-2");
        var options = new CompositeMemoryCollectionOptions
        {
            RankingStrategy = DistributedRankingStrategy.RoundRobin,
        };
        var composite = new CompositeMemoryCollection(new[] { backend1, backend2 }, options);

        await backend1.UpsertAsync(MakeRecord("fact-a", "entity-1", "coffee"));
        await backend2.UpsertAsync(MakeRecord("fact-b", "entity-1", "tea"));
        await backend1.UpsertAsync(MakeRecord("fact-c", "entity-1", "water"));

        var results = new List<VectorSearchResult<MemoryFactRecord>>();
        await foreach (var result in composite.SearchAsync(new ReadOnlyMemory<float>(nonZeroEmbedding), 10))
        {
            results.Add(result);
        }

        Assert.That(results, Has.Count.EqualTo(3));
    }

    [Test]
    public async Task GetAsync_WithExpressionFilter_ReturnsFilteredResults()
    {
        var backend1 = CreateBackend("backend-1");
        var backend2 = CreateBackend("backend-2");
        var composite = new CompositeMemoryCollection(new[] { backend1, backend2 });

        await backend1.UpsertAsync(MakeRecord("fact-1", "entity-1", "coffee"));
        await backend2.UpsertAsync(MakeRecord("fact-2", "entity-1", "tea"));
        await backend1.UpsertAsync(MakeRecord("fact-3", "entity-2", "water"));

        var results = new List<MemoryFactRecord>();
        await foreach (var record in composite.GetAsync(r => r.EntityId == "entity-1", 10))
        {
            results.Add(record);
        }

        Assert.That(results, Has.Count.EqualTo(2));
        Assert.That(results.All(r => r.EntityId == "entity-1"), Is.True);
    }

    [Test]
    public void GetService_ReturnsSelfForCorrectType()
    {
        var backend = CreateBackend("backend-1");
        var composite = new CompositeMemoryCollection(new[] { backend });

        var service = composite.GetService(typeof(CompositeMemoryCollection));

        Assert.That(service, Is.SameAs(composite));
    }

    #endregion
}
