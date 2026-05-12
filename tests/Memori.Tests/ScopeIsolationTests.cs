using Memori.Abstractions;
using Memori.Models;
using Microsoft.Extensions.VectorData;
using NUnit.Framework;

namespace Memori.Tests;

public sealed class ScopeIsolationTests
{
    [Test]
    public async Task RecallAsync_WithScope_ReturnsOnlyScopedFacts()
    {
        var memori = TestMemoriFactory.Create();
        var factCollection = GetFactCollection(memori);
        var conversationStorage = GetConversationStorage(memori);
        var entityId = await conversationStorage.GetOrCreateEntityAsync("entity-1");

        await factCollection.UpsertAsync(new MemoryFactRecord
        {
            Id = Guid.NewGuid().ToString("N"),
            EntityId = entityId,
            Content = "coffee is preferred",
            Embedding = new ReadOnlyMemory<float>(new float[1536]),
            MemoryType = "preference",
            Scope = "workspace-a",
        });
        await factCollection.UpsertAsync(new MemoryFactRecord
        {
            Id = Guid.NewGuid().ToString("N"),
            EntityId = entityId,
            Content = "tea is preferred",
            Embedding = new ReadOnlyMemory<float>(new float[1536]),
            MemoryType = "preference",
            Scope = "workspace-b",
        });

        memori.Attribution("entity-1");
        memori.SetScope("workspace-a");
        var results = await memori.RecallAsync("preferred");

        Assert.That(results, Has.Count.EqualTo(1));
        Assert.That(results.First().Content, Does.Contain("coffee"));
    }

    [Test]
    public async Task RecallAsync_WithoutScope_ReturnsFactsFromAllScopes()
    {
        var memori = TestMemoriFactory.Create();
        var factCollection = GetFactCollection(memori);
        var conversationStorage = GetConversationStorage(memori);
        var entityId = await conversationStorage.GetOrCreateEntityAsync("entity-1");

        await factCollection.UpsertAsync(new MemoryFactRecord
        {
            Id = Guid.NewGuid().ToString("N"),
            EntityId = entityId,
            Content = "coffee is preferred",
            Embedding = new ReadOnlyMemory<float>(new float[1536]),
            MemoryType = "preference",
            Scope = "workspace-a",
        });
        await factCollection.UpsertAsync(new MemoryFactRecord
        {
            Id = Guid.NewGuid().ToString("N"),
            EntityId = entityId,
            Content = "tea is preferred",
            Embedding = new ReadOnlyMemory<float>(new float[1536]),
            MemoryType = "preference",
            Scope = "workspace-b",
        });

        memori.Attribution("entity-1");
        var results = await memori.RecallAsync("preferred");

        Assert.That(results, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task RecallAsync_WithDifferentScope_ReturnsNoFacts()
    {
        var memori = TestMemoriFactory.Create();
        var factCollection = GetFactCollection(memori);
        var conversationStorage = GetConversationStorage(memori);
        var entityId = await conversationStorage.GetOrCreateEntityAsync("entity-1");

        await factCollection.UpsertAsync(new MemoryFactRecord
        {
            Id = Guid.NewGuid().ToString("N"),
            EntityId = entityId,
            Content = "coffee is preferred",
            Embedding = new ReadOnlyMemory<float>(new float[1536]),
            MemoryType = "preference",
            Scope = "workspace-a",
        });

        memori.Attribution("entity-1");
        memori.SetScope("workspace-z");
        var results = await memori.RecallAsync("coffee");

        Assert.That(results, Is.Empty);
    }

    [Test]
    public async Task SetScope_AndClearScope_WorksCorrectly()
    {
        var memori = TestMemoriFactory.Create();
        memori.SetScope("workspace-a");
        Assert.That(memori.CurrentScope, Is.EqualTo("workspace-a"));

        memori.ClearScope();
        Assert.That(memori.CurrentScope, Is.Null);
    }

    [Test]
    public async Task DeleteEntityMemoriesAsync_WithScope_OnlyDeletesScopedFacts()
    {
        var memori = TestMemoriFactory.Create();
        var factCollection = GetFactCollection(memori);
        var conversationStorage = GetConversationStorage(memori);
        var entityId = await conversationStorage.GetOrCreateEntityAsync("entity-1");

        await factCollection.UpsertAsync(new MemoryFactRecord
        {
            Id = "fact-1",
            EntityId = entityId,
            Content = "coffee",
            Embedding = new ReadOnlyMemory<float>(new float[1536]),
            Scope = "workspace-a",
        });
        await factCollection.UpsertAsync(new MemoryFactRecord
        {
            Id = "fact-2",
            EntityId = entityId,
            Content = "tea",
            Embedding = new ReadOnlyMemory<float>(new float[1536]),
            Scope = "workspace-b",
        });

        memori.Attribution("entity-1");
        memori.SetScope("workspace-a");
        await memori.DeleteEntityMemoriesAsync();

        var remainingFact = await factCollection.GetAsync("fact-2");
        Assert.That(remainingFact, Is.Not.Null);
        Assert.That(remainingFact!.Content, Is.EqualTo("tea"));
    }

    static VectorStoreCollection<string, MemoryFactRecord> GetFactCollection(MemoriEngine memori)
    {
        var field = typeof(MemoriEngine).GetField("factCollection", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        return (VectorStoreCollection<string, MemoryFactRecord>)field!.GetValue(memori)!;
    }

    static IConversationStorage GetConversationStorage(MemoriEngine memori)
    {
        var field = typeof(MemoriEngine).GetField("conversationStorage", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        return (IConversationStorage)field!.GetValue(memori)!;
    }
}
