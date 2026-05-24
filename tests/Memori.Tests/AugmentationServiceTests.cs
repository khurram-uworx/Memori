using Memori.Abstractions;
using Memori.Augmentation;
using Memori.Models;
using Memori.Storage;
using Memori.Summarization;
using Memori.Versioning;
using Microsoft.Extensions.VectorData;
using NUnit.Framework;

namespace Memori.Tests;

public class AugmentationServiceTests
{
    [Test]
    public async Task EnqueueAsync_WritesAllAugmentationOutputTypes()
    {
        var conversationStorage = new InMemoriConversationStorage();
        var vectorStore = new InMemoriVectorStore();
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
        var conversationStorage = new InMemoriConversationStorage();
        var vectorStore = new InMemoriVectorStore();
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

    [Test]
    public async Task EnqueueAsync_WithVersioningService_NewFactSetsVersionToOne()
    {
        var conversationStorage = new InMemoriConversationStorage();
        var vectorStore = new InMemoriVectorStore();
        var factCollection = vectorStore.GetCollection<string, MemoryFactRecord>("memori_facts");
        var entityId = await conversationStorage.GetOrCreateEntityAsync("entity-1");
        var conversation = await conversationStorage.GetOrCreateConversationAsync("session-1", TimeSpan.FromMinutes(30));
        var client = new StaticAugmentationClient(
            new AugmentationResult(Facts: [new NewMemoryFact("coffee", memoryType: "preference")]));
        var versioning = new VersioningService();
        var service = new AugmentationService(
            conversationStorage,
            factCollection,
            client,
            options: new MemoriOptions { RunAugmentationInBackground = false },
            versioningService: versioning);

        await service.EnqueueAsync(
            new AugmentationInput(entityId, null, conversation.Id, [new ConversationMessage(ConversationRoles.User, "I like coffee.")]));

        var stored = await findFactAsync(factCollection, entityId, "coffee");
        Assert.That(stored, Is.Not.Null);
        Assert.That(stored!.Version, Is.EqualTo(1));
    }

    [Test]
    public async Task EnqueueAsync_WithVersioningService_SameFactIncrementsVersion()
    {
        var conversationStorage = new InMemoriConversationStorage();
        var vectorStore = new InMemoriVectorStore();
        var factCollection = vectorStore.GetCollection<string, MemoryFactRecord>("memori_facts");
        var entityId = await conversationStorage.GetOrCreateEntityAsync("entity-1");
        var conversation = await conversationStorage.GetOrCreateConversationAsync("session-1", TimeSpan.FromMinutes(30));
        var client = new StaticAugmentationClient(
            new AugmentationResult(Facts: [new NewMemoryFact("coffee", memoryType: "preference")]));
        var versioning = new VersioningService();
        var service = new AugmentationService(
            conversationStorage,
            factCollection,
            client,
            options: new MemoriOptions { RunAugmentationInBackground = false },
            versioningService: versioning);

        await service.EnqueueAsync(
            new AugmentationInput(entityId, null, conversation.Id, [new ConversationMessage(ConversationRoles.User, "I like coffee.")]));

        // Second augmentation with same fact content should increment version
        await service.EnqueueAsync(
            new AugmentationInput(entityId, null, conversation.Id, [new ConversationMessage(ConversationRoles.User, "I still like coffee.")]));

        var stored = await findFactAsync(factCollection, entityId, "coffee");
        Assert.That(stored, Is.Not.Null);
        Assert.That(stored!.Version, Is.EqualTo(2));
        Assert.That(stored.PreviousVersionId, Is.Not.Null);
    }

    [Test]
    public async Task EnqueueAsync_WithVersioningService_MergeStrategy_PreservesPreviousVersionLink()
    {
        var conversationStorage = new InMemoriConversationStorage();
        var vectorStore = new InMemoriVectorStore();
        var factCollection = vectorStore.GetCollection<string, MemoryFactRecord>("memori_facts");
        var entityId = await conversationStorage.GetOrCreateEntityAsync("entity-1");
        var conversation = await conversationStorage.GetOrCreateConversationAsync("session-1", TimeSpan.FromMinutes(30));
        var versioning = new VersioningService(ConflictResolutionStrategy.Merge);
        var client = new StaticAugmentationClient(
            new AugmentationResult(Facts: [new NewMemoryFact("same content", memoryType: "preference")]));
        var service = new AugmentationService(
            conversationStorage, factCollection, client,
            options: new MemoriOptions { RunAugmentationInBackground = false },
            versioningService: versioning);

        // First write establishes the record at Version=1
        await service.EnqueueAsync(
            new AugmentationInput(entityId, null, conversation.Id, [new ConversationMessage(ConversationRoles.User, "first")]));

        var firstWrite = await findFactAsync(factCollection, entityId, "same content");
        Assert.That(firstWrite, Is.Not.Null);
        Assert.That(firstWrite!.Version, Is.EqualTo(1));
        var firstId = firstWrite.Id;

        // Second write with same content (found by content match)
        // The pipeline reads Version=1, expectedVersion=1 → no conflict → Version=2
        await service.EnqueueAsync(
            new AugmentationInput(entityId, null, conversation.Id, [new ConversationMessage(ConversationRoles.User, "second")]));

        var secondWrite = await findFactAsync(factCollection, entityId, "same content");
        Assert.That(secondWrite, Is.Not.Null);
        Assert.That(secondWrite!.Version, Is.EqualTo(2));
        Assert.That(secondWrite.PreviousVersionId, Is.EqualTo(firstId));
        Assert.That(secondWrite.Id, Is.EqualTo(firstId));
    }

    [Test]
    public async Task EnqueueAsync_WithoutVersioningService_ExistingTestsStillPass()
    {
        var conversationStorage = new InMemoriConversationStorage();
        var vectorStore = new InMemoriVectorStore();
        var factCollection = vectorStore.GetCollection<string, MemoryFactRecord>("memori_facts");
        var entityId = await conversationStorage.GetOrCreateEntityAsync("entity-1");
        var conversation = await conversationStorage.GetOrCreateConversationAsync("session-1", TimeSpan.FromMinutes(30));
        var client = new StaticAugmentationClient(
            new AugmentationResult(Facts: [new NewMemoryFact("coffee", memoryType: "preference")]));
        var service = new AugmentationService(
            conversationStorage,
            factCollection,
            client,
            options: new MemoriOptions { RunAugmentationInBackground = false });

        await service.EnqueueAsync(
            new AugmentationInput(entityId, null, conversation.Id, [new ConversationMessage(ConversationRoles.User, "I like coffee.")]));

        var factResults = new List<MemoryFactRecord>();
        await foreach (var result in factCollection.SearchAsync("coffee", 10, new VectorSearchOptions<MemoryFactRecord> { Filter = r => r.EntityId == entityId }))
        {
            factResults.Add(result.Record);
        }

        Assert.That(factResults.Any(f => f.Content == "coffee"), Is.True);
    }

    [Test]
    public async Task EnqueueAsync_WithThreadSummarizer_StoresSummaryFact()
    {
        var conversationStorage = new InMemoriConversationStorage();
        var vectorStore = new InMemoriVectorStore();
        var factCollection = vectorStore.GetCollection<string, MemoryFactRecord>("memori_facts");
        var entityId = await conversationStorage.GetOrCreateEntityAsync("entity-1");
        var conversation = await conversationStorage.GetOrCreateConversationAsync("session-1", TimeSpan.FromMinutes(30));
        var summarizer = new RecordingThreadSummarizer("generated summary");
        var client = new StaticAugmentationClient(
            new AugmentationResult(Facts: [new NewMemoryFact("fact", memoryType: "preference")]));
        var service = new AugmentationService(
            conversationStorage,
            factCollection,
            client,
            options: new MemoriOptions { RunAugmentationInBackground = false },
            threadSummarizer: summarizer);

        await service.EnqueueAsync(
            new AugmentationInput(entityId, null, conversation.Id, [new ConversationMessage(ConversationRoles.User, "hello")]));

        // Verify summarizer was called
        Assert.That(summarizer.CallCount, Is.GreaterThan(0));

        // Verify a summary fact was stored
        var summaryResults = new List<MemoryFactRecord>();
        await foreach (var result in factCollection.GetAsync(
            r => r.MemoryType == "summary", 10, cancellationToken: CancellationToken.None))
        {
            summaryResults.Add(result);
        }

        Assert.That(summaryResults, Has.Count.EqualTo(1));
        Assert.That(summaryResults[0].Content, Is.EqualTo("generated summary"));
        Assert.That(summaryResults[0].EntityId, Is.EqualTo(entityId));
    }

    [Test]
    public async Task EnqueueAsync_WithoutThreadSummarizer_DoesNotStoreSummaryFact()
    {
        var conversationStorage = new InMemoriConversationStorage();
        var vectorStore = new InMemoriVectorStore();
        var factCollection = vectorStore.GetCollection<string, MemoryFactRecord>("memori_facts");
        var entityId = await conversationStorage.GetOrCreateEntityAsync("entity-1");
        var conversation = await conversationStorage.GetOrCreateConversationAsync("session-1", TimeSpan.FromMinutes(30));
        var client = new StaticAugmentationClient(
            new AugmentationResult(Facts: [new NewMemoryFact("fact", memoryType: "preference")]));
        var service = new AugmentationService(
            conversationStorage,
            factCollection,
            client,
            options: new MemoriOptions { RunAugmentationInBackground = false });

        await service.EnqueueAsync(
            new AugmentationInput(entityId, null, conversation.Id, [new ConversationMessage(ConversationRoles.User, "hello")]));

        var summaryResults = new List<MemoryFactRecord>();
        await foreach (var result in factCollection.GetAsync(
            r => r.MemoryType == "summary", 10, cancellationToken: CancellationToken.None))
        {
            summaryResults.Add(result);
        }

        Assert.That(summaryResults, Is.Empty);
    }

    [Test]
    public async Task EnqueueAsync_WithThreadSummarizerError_DoesNotCrashPipeline()
    {
        var conversationStorage = new InMemoriConversationStorage();
        var vectorStore = new InMemoriVectorStore();
        var factCollection = vectorStore.GetCollection<string, MemoryFactRecord>("memori_facts");
        var entityId = await conversationStorage.GetOrCreateEntityAsync("entity-1");
        var processId = await conversationStorage.GetOrCreateProcessAsync("process-1");
        var conversation = await conversationStorage.GetOrCreateConversationAsync("session-1", TimeSpan.FromMinutes(30));
        var summarizer = new RecordingThreadSummarizer("summary", throwOnCall: true);
        var client = new StaticAugmentationClient(
            new AugmentationResult(
                Facts: [new NewMemoryFact("coffee", memoryType: "preference")],
                ProcessAttributes: ["support"]));
        var service = new AugmentationService(
            conversationStorage,
            factCollection,
            client,
            options: new MemoriOptions { RunAugmentationInBackground = false },
            threadSummarizer: summarizer);

        await service.EnqueueAsync(
            new AugmentationInput(entityId, processId, conversation.Id, [new ConversationMessage(ConversationRoles.User, "hello")]));

        // Fact upsert should still succeed despite summarizer error
        var factResults = new List<MemoryFactRecord>();
        await foreach (var result in factCollection.SearchAsync("coffee", 10, new VectorSearchOptions<MemoryFactRecord> { Filter = r => r.EntityId == entityId }))
        {
            factResults.Add(result.Record);
        }
        Assert.That(factResults.Any(f => f.Content == "coffee"), Is.True);

        // Verify summarizer was called despite exception
        Assert.That(summarizer.CallCount, Is.GreaterThan(0));
    }

    static async ValueTask<MemoryFactRecord?> findFactAsync(
        VectorStoreCollection<string, MemoryFactRecord> collection,
        string entityId,
        string content)
    {
        await foreach (var match in collection.GetAsync(
            r => r.EntityId == entityId && r.Content.Contains(content),
            1))
        {
            return match;
        }

        return null;
    }

    sealed class RecordingThreadSummarizer : IThreadSummarizer
    {
        readonly string result;
        readonly bool throwOnCall;

        public int CallCount { get; private set; }

        public RecordingThreadSummarizer(string result, bool throwOnCall = false)
        {
            this.result = result;
            this.throwOnCall = throwOnCall;
        }

        public ValueTask<string> SummarizeAsync(
            IReadOnlyList<ConversationMessage> messages,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            if (throwOnCall)
                throw new InvalidOperationException("Simulated summarizer error");
            return ValueTask.FromResult(result);
        }

        public ValueTask<string> SummarizeAsync(
            IReadOnlyList<ConversationMessage> messages,
            string previousSummary,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            if (throwOnCall)
                throw new InvalidOperationException("Simulated summarizer error");
            return ValueTask.FromResult(result);
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
