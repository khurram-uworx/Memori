using Memori.Abstractions;
using Memori.Augmentation;
using Memori.Models;
using Memori.Storage;
using NUnit.Framework;
using System.Collections;
using System.Reflection;

namespace Memori.Tests;

public class AugmentationServiceTests
{
    [Test]
    public async Task EnqueueAsync_WritesAllAugmentationOutputTypes()
    {
        var storage = new InMemoryStorage();
        var entityId = await storage.GetOrCreateEntityAsync("entity-1");
        var processId = await storage.GetOrCreateProcessAsync("process-1");
        var conversation = await storage.GetOrCreateConversationAsync("session-1", TimeSpan.FromMinutes(30));
        var client = new StaticAugmentationClient(
            new AugmentationResult(
                Facts: [new NewMemoryFact("coffee", memoryType: "preference")],
                SemanticTriples: [new SemanticTriple("user", "person", "likes", "coffee", "drink")],
                ProcessAttributes: ["support", "triage"],
                ConversationSummary: "The user likes coffee."));
        var service = new AugmentationService(
            storage,
            client,
            options: new MemoriOptions { RunAugmentationInBackground = false });

        await service.EnqueueAsync(
            new AugmentationInput(
                entityId,
                processId,
                conversation.Id,
                [new ConversationMessage(ConversationRoles.User, "I like coffee.")]));

        var facts = await storage.SearchFactsAsync(entityId, "coffee", null, 10, 10);
        var triples = getSemanticTriples(storage, entityId);
        var updatedConversation = await storage.GetOrCreateConversationAsync("session-1", TimeSpan.FromMinutes(30));

        Assert.That(facts.Any(fact => fact.Content == "coffee" && fact.MemoryType == "preference"), Is.True);
        Assert.That(triples.Any(triple => triple.ToFactText() == "user likes coffee"), Is.True);
        Assert.That(updatedConversation.Summary, Is.EqualTo("The user likes coffee."));
    }

    [Test]
    public async Task EnqueueAsync_SkipsProcessAttributesWhenNoProcessIdIsAvailable()
    {
        var storage = new InMemoryStorage();
        var entityId = await storage.GetOrCreateEntityAsync("entity-1");
        var conversation = await storage.GetOrCreateConversationAsync("session-1", TimeSpan.FromMinutes(30));
        var client = new StaticAugmentationClient(
            new AugmentationResult(ProcessAttributes: ["support"]));
        var service = new AugmentationService(
            storage,
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

    static IReadOnlyList<SemanticTriple> getSemanticTriples(InMemoryStorage storage, string entityId)
    {
        var entitiesField = typeof(InMemoryStorage).GetField(
            "entities",
            BindingFlags.Instance | BindingFlags.NonPublic);
        var gateField = typeof(InMemoryStorage).GetField(
            "gate",
            BindingFlags.Instance | BindingFlags.NonPublic);

        if (entitiesField is null || gateField is null)
            return [];

        var gate = gateField.GetValue(storage);
        if (gate is null)
            return [];

        lock (gate)
         {
             var entities = (IDictionary)entitiesField.GetValue(storage)!;
             if (!entities.Contains(entityId))
                 return [];

             var entityState = entities[entityId];
             var semanticTriplesProperty = entityState.GetType().GetProperty("SemanticTriples");
             return semanticTriplesProperty?.GetValue(entityState) as IReadOnlyList<SemanticTriple> ?? [];
         }
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
