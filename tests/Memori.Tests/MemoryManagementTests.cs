using Memori.Management;
using Memori.Models;
using Memori.Storage;
using Microsoft.Extensions.VectorData;
using NUnit.Framework;

namespace Memori.Tests;

public sealed class MemoryManagementTests
{
    static MemoryManagementService CreateService(out VectorStoreCollection<string, MemoryFactRecord> factCollection)
    {
        var vectorStore = new InMemoriVectorStore();
        factCollection = vectorStore.GetCollection<string, MemoryFactRecord>("memori_facts");
        return new MemoryManagementService(factCollection);
    }

    static MemoryFactRecord CreateFact(string id, string content, string entityId = "entity-1",
        string? memoryType = null, string? scope = null)
        => new()
        {
            Id = id,
            EntityId = entityId,
            Content = content,
            Embedding = new ReadOnlyMemory<float>(new float[1536]),
            MemoryType = memoryType ?? "general",
            Scope = scope,
        };

    [Test]
    public async Task ListMemoriesAsync_ReturnsAllMemories()
    {
        var service = CreateService(out var factCollection);
        await factCollection.UpsertAsync(CreateFact("1", "coffee"));
        await factCollection.UpsertAsync(CreateFact("2", "tea"));

        var memories = await service.ListMemoriesAsync("entity-1");

        Assert.That(memories, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task ListMemoriesAsync_PaginatesCorrectly()
    {
        var service = CreateService(out var factCollection);
        for (int i = 0; i < 10; i++)
            await factCollection.UpsertAsync(CreateFact($"id-{i}", $"content-{i}"));

        var page1 = await service.ListMemoriesAsync("entity-1", skip: 0, take: 3);
        var page2 = await service.ListMemoriesAsync("entity-1", skip: 3, take: 3);

        Assert.That(page1, Has.Count.EqualTo(3));
        Assert.That(page2, Has.Count.EqualTo(3));
    }

    [Test]
    public async Task ListMemoriesAsync_SkipsSoftDeleted()
    {
        var service = CreateService(out var factCollection);
        await factCollection.UpsertAsync(CreateFact("1", "active"));
        var deleted = CreateFact("2", "deleted");
        deleted.IsDeleted = true;
        await factCollection.UpsertAsync(deleted);

        var memories = await service.ListMemoriesAsync("entity-1");

        Assert.That(memories, Has.Count.EqualTo(1));
        Assert.That(memories.First().Id, Is.EqualTo("1"));
    }

    [Test]
    public async Task SearchMemoriesAsync_FindsMatchingContent()
    {
        var service = CreateService(out var factCollection);
        await factCollection.UpsertAsync(CreateFact("1", "coffee is preferred", memoryType: "preference"));
        await factCollection.UpsertAsync(CreateFact("2", "tea is nice", memoryType: "preference"));

        var results = await service.SearchMemoriesAsync("entity-1", "coffee");

        Assert.That(results, Has.Count.EqualTo(1));
        Assert.That(results.First().Content, Does.Contain("coffee"));
    }

    [Test]
    public async Task SearchMemoriesAsync_FiltersByType()
    {
        var service = CreateService(out var factCollection);
        await factCollection.UpsertAsync(CreateFact("1", "coffee", memoryType: "preference"));
        await factCollection.UpsertAsync(CreateFact("2", "paris", memoryType: "profile"));

        var results = await service.SearchMemoriesAsync("entity-1", "coffee", memoryType: "preference");

        Assert.That(results, Has.Count.EqualTo(1));
        Assert.That(results.First().MemoryType, Is.EqualTo("preference"));
    }

    [Test]
    public async Task SearchMemoriesAsync_FiltersByScope()
    {
        var service = CreateService(out var factCollection);
        await factCollection.UpsertAsync(CreateFact("1", "coffee", scope: "workspace-a"));
        await factCollection.UpsertAsync(CreateFact("2", "coffee", scope: "workspace-b"));

        var results = await service.SearchMemoriesAsync("entity-1", "coffee", scope: "workspace-a");

        Assert.That(results, Has.Count.EqualTo(1));
        Assert.That(results.First().Scope, Is.EqualTo("workspace-a"));
    }

    [Test]
    public async Task GetMemoryAsync_ReturnsRecordWhenFound()
    {
        var service = CreateService(out var factCollection);
        await factCollection.UpsertAsync(CreateFact("fact-1", "hello"));

        var record = await service.GetMemoryAsync("fact-1");

        Assert.That(record, Is.Not.Null);
        Assert.That(record!.Content, Is.EqualTo("hello"));
    }

    [Test]
    public async Task GetMemoryAsync_ReturnsNullWhenNotFound()
    {
        var service = CreateService(out _);

        var record = await service.GetMemoryAsync("nonexistent");

        Assert.That(record, Is.Null);
    }

    [Test]
    public async Task UpdateMemoryAsync_UpdatesContent()
    {
        var service = CreateService(out var factCollection);
        await factCollection.UpsertAsync(CreateFact("fact-1", "original"));

        var success = await service.UpdateMemoryAsync("fact-1", "updated content");

        Assert.That(success, Is.True);
        var record = await factCollection.GetAsync("fact-1");
        Assert.That(record!.Content, Is.EqualTo("updated content"));
    }

    [Test]
    public async Task UpdateMemoryAsync_ReturnsFalseWhenNotFound()
    {
        var service = CreateService(out _);

        var success = await service.UpdateMemoryAsync("nonexistent", "content");

        Assert.That(success, Is.False);
    }

    [Test]
    public async Task SoftDeleteMemoryAsync_MarksRecordAsDeleted()
    {
        var service = CreateService(out var factCollection);
        await factCollection.UpsertAsync(CreateFact("fact-1", "content"));

        var success = await service.SoftDeleteMemoryAsync("fact-1");

        Assert.That(success, Is.True);
        var record = await factCollection.GetAsync("fact-1");
        Assert.That(record!.IsDeleted, Is.True);
    }

    [Test]
    public async Task SoftDeleteMemoryAsync_ExcludesFromList()
    {
        var service = CreateService(out var factCollection);
        await factCollection.UpsertAsync(CreateFact("1", "visible"));
        await factCollection.UpsertAsync(CreateFact("2", "hidden"));

        await service.SoftDeleteMemoryAsync("2");

        var memories = await service.ListMemoriesAsync("entity-1");
        Assert.That(memories, Has.Count.EqualTo(1));
        Assert.That(memories.First().Id, Is.EqualTo("1"));
    }

    [Test]
    public async Task HardDeleteMemoryAsync_RemovesRecord()
    {
        var service = CreateService(out var factCollection);
        await factCollection.UpsertAsync(CreateFact("fact-1", "content"));

        var success = await service.HardDeleteMemoryAsync("fact-1");

        Assert.That(success, Is.True);
        var record = await factCollection.GetAsync("fact-1");
        Assert.That(record, Is.Null);
    }

    [Test]
    public async Task HardDeleteMemoryAsync_ReturnsFalseWhenNotFound()
    {
        var service = CreateService(out _);

        var success = await service.HardDeleteMemoryAsync("nonexistent");

        Assert.That(success, Is.False);
    }

    [Test]
    public async Task RestoreMemoryAsync_ReactivatesSoftDeleted()
    {
        var service = CreateService(out var factCollection);
        var fact = CreateFact("fact-1", "content");
        fact.IsDeleted = true;
        await factCollection.UpsertAsync(fact);

        var success = await service.RestoreMemoryAsync("fact-1");

        Assert.That(success, Is.True);
        var record = await factCollection.GetAsync("fact-1");
        Assert.That(record!.IsDeleted, Is.False);
    }

    [Test]
    public async Task GetMemoryCountAsync_CountsCorrectly()
    {
        var service = CreateService(out var factCollection);
        await factCollection.UpsertAsync(CreateFact("1", "a"));
        await factCollection.UpsertAsync(CreateFact("2", "b"));

        var count = await service.GetMemoryCountAsync("entity-1");

        Assert.That(count, Is.EqualTo(2));
    }

    [Test]
    public async Task GetMemoryCountAsync_ExcludesSoftDeletedByDefault()
    {
        var service = CreateService(out var factCollection);
        await factCollection.UpsertAsync(CreateFact("1", "a"));
        var deleted = CreateFact("2", "b");
        deleted.IsDeleted = true;
        await factCollection.UpsertAsync(deleted);

        var count = await service.GetMemoryCountAsync("entity-1");

        Assert.That(count, Is.EqualTo(1));
    }

    [Test]
    public async Task GetMemoryCountAsync_IncludesSoftDeletedWhenRequested()
    {
        var service = CreateService(out var factCollection);
        await factCollection.UpsertAsync(CreateFact("1", "a"));
        var deleted = CreateFact("2", "b");
        deleted.IsDeleted = true;
        await factCollection.UpsertAsync(deleted);

        var count = await service.GetMemoryCountAsync("entity-1", includeDeleted: true);

        Assert.That(count, Is.EqualTo(2));
    }
}
