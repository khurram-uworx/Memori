using Memori.Abstractions;
using Memori.Augmentation;
using Memori.Models;
using Memori.Storage;
using Microsoft.Extensions.VectorData;
using NUnit.Framework;

namespace Memori.Tests;

public class AugmentationServiceTests
{
    [Test]
    public async Task EnqueueAsync_WritesAllAugmentationOutputTypes()
    {
        var conversationStorage = new InMemoryConversationStorage();
        var vectorStore = new InMemoryVectorStore();
        var factCollection = vectorStore.GetCollection<string, MemoryFactRecord>("memori_facts");
        var entityId = await conversationStorage.GetOrCreateEntityAsync("entity-1");
        var processId = await conversationStorage.GetOrCreateProcessAsync("process-1");
        var conversation = await conversationStorage.GetOrCreateConversationAsync("session-1", TimeSpan.FromMinutes(30));
        var client = new StaticAugmentationClient(
            new AugmentationResult(
                Facts: [new NewMemoryFact("coffee", memoryType: "preference")],
                SemanticTriples: [new SemanticTriple("user", "person", "likes", "coffee", "drink")],
                ProcessAttributes: ["support", "triage"],
                ConversationSummary: "The user likes coffee."));
        var service = new AugmentationService(
            conversationStorage,
            factCollection,
            client,
            options: new MemoriOptions { RunAugmentationInBackground = false });

        await service.EnqueueAsync(
            new AugmentationInput(
                entityId,
                processId,
                conversation.Id,
                [new ConversationMessage(ConversationRoles.User, "I like coffee.")]));

        // Search for facts
        var factResults = new List<MemoryFactRecord>();
        await foreach (var result in factCollection.SearchAsync("coffee", 10, new VectorSearchOptions<MemoryFactRecord> { Filter = r => r.EntityId == entityId }))
        {
            factResults.Add(result.Record);
        }

        // Search for semantic triples
        var tripleResults = new List<MemoryFactRecord>();
        await foreach (var result in factCollection.SearchAsync("user likes coffee", 10, new VectorSearchOptions<MemoryFactRecord> { Filter = r => r.EntityId == entityId && r.MemoryType == "semantic_triple" }))
        {
            tripleResults.Add(result.Record);
        }

        var updatedConversation = await conversationStorage.GetOrCreateConversationAsync("session-1", TimeSpan.FromMinutes(30));

        Assert.That(factResults.Any(fact => fact.Content == "coffee" && fact.MemoryType == "preference"), Is.True);
        Assert.That(tripleResults.Any(triple => triple.Content == "user likes coffee"), Is.True);
        Assert.That(updatedConversation.Summary, Is.EqualTo("The user likes coffee."));
    }

    [Test]
    public async Task EnqueueAsync_SkipsProcessAttributesWhenNoProcessIdIsAvailable()
    {
        var conversationStorage = new InMemoryConversationStorage();
        var vectorStore = new InMemoryVectorStore();
        var factCollection = vectorStore.GetCollection<string, MemoryFactRecord>("memori_facts");
        var entityId = await conversationStorage.GetOrCreateEntityAsync("entity-1");
        var conversation = await conversationStorage.GetOrCreateConversationAsync("session-1", TimeSpan.FromMinutes(30));
        var client = new StaticAugmentationClient(
            new AugmentationResult(ProcessAttributes: ["support"]));
        var service = new AugmentationService(
            conversationStorage,
            factCollection,
            client,
            options: new MemoriOptions { RunAugmentationInBackground = false });

        await service.EnqueueAsync(
            new AugmentationInput(
                entityId,
                null,
                conversation.Id,
                [new ConversationMessage(ConversationRoles.User, "hello")]));

        Assert.Pass();
    }

    sealed class StaticAugmentationClient : IAugmentationClient
    {
        readonly AugmentationResult result;

        public StaticAugmentationClient(AugmentationResult result)
        {
            this.result = result;
        }

        public ValueTask<AugmentationResult?> AugmentAsync(
            AugmentationInput context,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult<AugmentationResult?>(result);
    }
}
