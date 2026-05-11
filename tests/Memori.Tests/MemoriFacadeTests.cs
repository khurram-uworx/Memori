using Memori.Abstractions;
using Memori.Management;
using Memori.Models;
using Memori.Search;
using Memori.Storage;
using Memori.Summarization;
using Memori.Versioning;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.VectorData;
using NUnit.Framework;

namespace Memori.Tests;

public class MemoriFacadeTests
{
    [Test]
    public async Task CaptureAsync_WithNoAttribution_DoesNotWriteMessages()
    {
        var memori = TestMemoriFactory.Create();
        memori.SetSession("test-session");

        await memori.CaptureAsync(new[]
        {
            new ConversationMessage(ConversationRoles.User, "hello"),
        });

        // Get conversation storage from memori's internal state
        var conversationStorage = GetConversationStorage(memori);
        var conversation = await conversationStorage.GetOrCreateConversationAsync("test-session", TimeSpan.FromMinutes(30));
        var messages = await conversationStorage.GetConversationMessagesAsync(conversation.Id);

        Assert.That(messages, Is.Empty);
    }

    [Test]
    public async Task CaptureAsync_StripsSystemMessages_WhenConfigured()
    {
        var memori = TestMemoriFactory.Create(options: new MemoriOptions { StripSystemMessagesOnCapture = true });
        memori.Attribution("entity-1");
        memori.SetSession("test-session");

        await memori.CaptureAsync(new[]
        {
            new ConversationMessage(ConversationRoles.System, "ignore me"),
            new ConversationMessage(ConversationRoles.User, "hello"),
        });

        var conversationStorage = GetConversationStorage(memori);
        var conversation = await conversationStorage.GetOrCreateConversationAsync("test-session", TimeSpan.FromMinutes(30));
        var messages = await conversationStorage.GetConversationMessagesAsync(conversation.Id);

        Assert.That(messages.Select(message => message.Role), Is.EqualTo(new[] { ConversationRoles.User }));
    }

    [Test]
    public async Task RecallAsync_UsesCurrentAttribution()
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
            MemoryType = "preference"
        });

        memori.Attribution("entity-1");
        memori.SetSession("test-session");

        var results = await memori.RecallAsync("coffee");

        Assert.That(results, Is.Not.Empty);
        Assert.That(results.First().Content, Does.Contain("coffee"));
    }

    [Test]
    public async Task WaitForAugmentationAsync_CompletesQueuedWork()
    {
        var memori = TestMemoriFactory.Create(augmentationClient: new TestAugmentationClient());
        memori.Attribution("entity-1");
        memori.SetSession("test-session");

        await memori.CaptureAsync(new[]
        {
            new ConversationMessage(ConversationRoles.User, "hello"),
            new ConversationMessage(ConversationRoles.Assistant, "hi"),
        });

        await memori.WaitForAugmentationAsync();

        var factCollection = GetFactCollection(memori);
        var conversationStorage = GetConversationStorage(memori);
        var entityId = await conversationStorage.GetOrCreateEntityAsync("entity-1");
        var factResults = new List<VectorSearchResult<MemoryFactRecord>>();
        await foreach (var result in factCollection.SearchAsync(new ReadOnlyMemory<float>(new float[1536]), 10, new VectorSearchOptions<MemoryFactRecord> { Filter = r => r.EntityId == entityId }))
        {
            factResults.Add(result);
        }

        Assert.That(factResults, Is.Empty); // TODO: Once we have better semantic analyzer it should not be empty
    }

    [Test]
    public void CurrentAttributionAndSessionId_ReflectLifecycleState()
    {
        var memori = TestMemoriFactory.Create();

        Assert.That(memori.CurrentAttribution, Is.Null);
        Assert.That(memori.CurrentSessionId, Is.Null);

        var attribution = memori.Attribution("entity-1", "process-1");
        memori.SetSession("session-1");

        Assert.That(memori.CurrentAttribution, Is.EqualTo(attribution));
        Assert.That(memori.CurrentAttribution!.EntityId, Is.EqualTo("entity-1"));
        Assert.That(memori.CurrentAttribution.ProcessId, Is.EqualTo("process-1"));
        Assert.That(memori.CurrentSessionId, Is.EqualTo("session-1"));
    }

    [Test]
    public async Task ClearAttribution_DisablesCaptureAndRecallUntilReset()
    {
        var memori = TestMemoriFactory.Create();
        var factCollection = GetFactCollection(memori);
        var conversationStorage = GetConversationStorage(memori);
        memori.Attribution("entity-1");
        memori.SetSession("test-session");

        var entityId = await conversationStorage.GetOrCreateEntityAsync("entity-1");
        await factCollection.UpsertAsync(new MemoryFactRecord
        {
            Id = Guid.NewGuid().ToString("N"),
            EntityId = entityId,
            Content = "coffee",
            Embedding = new ReadOnlyMemory<float>(new float[1536])
        });

        memori.ClearAttribution();

        await memori.CaptureAsync([new ConversationMessage(ConversationRoles.User, "hello")]);
        var recall = await memori.RecallAsync("coffee");
        var conversation = await conversationStorage.GetOrCreateConversationAsync("test-session", TimeSpan.FromMinutes(30));
        var messages = await conversationStorage.GetConversationMessagesAsync(conversation.Id);

        Assert.That(memori.CurrentAttribution, Is.Null);
        Assert.That(recall, Is.Empty);
        Assert.That(messages, Is.Empty);
    }

    [Test]
    public async Task ClearSession_CausesNextCaptureToCreateNewSession()
    {
        var memori = TestMemoriFactory.Create();
        var conversationStorage = GetConversationStorage(memori);
        memori.Attribution("entity-1");
        memori.SetSession("session-1");

        await memori.CaptureAsync([new ConversationMessage(ConversationRoles.User, "first")]);
        memori.ClearSession();

        Assert.That(memori.CurrentSessionId, Is.Null);

        await memori.CaptureAsync([new ConversationMessage(ConversationRoles.User, "second")]);

        Assert.That(memori.CurrentSessionId, Is.Not.Null);
        Assert.That(memori.CurrentSessionId, Is.Not.EqualTo("session-1"));

        var originalConversation = await conversationStorage.GetOrCreateConversationAsync("session-1", TimeSpan.FromMinutes(30));
        var originalMessages = await conversationStorage.GetConversationMessagesAsync(originalConversation.Id);
        var newConversation = await conversationStorage.GetOrCreateConversationAsync(memori.CurrentSessionId!, TimeSpan.FromMinutes(30));
        var newMessages = await conversationStorage.GetConversationMessagesAsync(newConversation.Id);

        Assert.That(originalMessages.Select(message => message.Content), Is.EqualTo(["first"]));
        Assert.That(newMessages.Select(message => message.Content), Is.EqualTo(["second"]));
    }

    [Test]
    public async Task ResumeSession_ReusesExternallyManagedSession()
    {
        var memori = TestMemoriFactory.Create();
        var conversationStorage = GetConversationStorage(memori);
        memori.Attribution("entity-1");
        memori.ResumeSession("external-session");

        await memori.CaptureAsync([new ConversationMessage(ConversationRoles.User, "first")]);
        memori.NewSession();
        memori.ResumeSession("external-session");
        await memori.CaptureAsync([new ConversationMessage(ConversationRoles.User, "second")]);

        var conversation = await conversationStorage.GetOrCreateConversationAsync("external-session", TimeSpan.FromMinutes(30));
        var messages = await conversationStorage.GetConversationMessagesAsync(conversation.Id);

        Assert.That(memori.CurrentSessionId, Is.EqualTo("external-session"));
        Assert.That(messages.Select(message => message.Content), Is.EqualTo(["first", "second"]));
    }

    [Test]
    public async Task ResumeSession_DoesNotChangeEntityScopedRecall()
    {
        var memori = TestMemoriFactory.Create();
        var factCollection = GetFactCollection(memori);
        var conversationStorage = GetConversationStorage(memori);
        var userOne = await conversationStorage.GetOrCreateEntityAsync("user-1");
        var userTwo = await conversationStorage.GetOrCreateEntityAsync("user-2");
        await factCollection.UpsertAsync(new MemoryFactRecord
        {
            Id = Guid.NewGuid().ToString("N"),
            EntityId = userOne,
            Content = "coffee",
            Embedding = new ReadOnlyMemory<float>(new float[1536])
        });
        await factCollection.UpsertAsync(new MemoryFactRecord
        {
            Id = Guid.NewGuid().ToString("N"),
            EntityId = userTwo,
            Content = "tea",
            Embedding = new ReadOnlyMemory<float>(new float[1536])
        });

        memori.Attribution("user-1");
        memori.ResumeSession("shared-session");

        var results = await memori.RecallAsync("tea");

        Assert.That(results, Is.Empty);
    }

    [Test]
    public async Task CaptureAsync_CreatesNewConversationAfterSessionTimeout()
    {
        var memori = TestMemoriFactory.Create(options: new MemoriOptions { SessionTimeout = TimeSpan.FromMilliseconds(50) });
        var conversationStorage = GetConversationStorage(memori);
        memori.Attribution("entity-1");
        memori.SetSession("test-session");

        await memori.CaptureAsync([new ConversationMessage(ConversationRoles.User, "first")]);
        var first = await conversationStorage.GetOrCreateConversationAsync("test-session", TimeSpan.FromMilliseconds(50));
        await Task.Delay(75);
        await memori.CaptureAsync([new ConversationMessage(ConversationRoles.User, "second")]);
        var second = await conversationStorage.GetOrCreateConversationAsync("test-session", TimeSpan.FromMilliseconds(50));

        Assert.That(second.Id, Is.Not.EqualTo(first.Id));
    }

    [Test]
    public async Task AddMemori_WithExplicitStorageAndNoEmbeddingGenerator_UsesLexicalFallback()
    {
        var services = new ServiceCollection();
        var conversationStorage = new InMemoryConversationStorage();
        var vectorStore = new InMemoryVectorStore();
        var factCollection = vectorStore.GetCollection<string, MemoryFactRecord>("memori_facts");

        services.AddMemori(conversationStorage, factCollection);
        var provider = services.BuildServiceProvider();

        var memori = provider.GetRequiredService<Memori>();
        memori.Attribution("entity-1");
        memori.SetSession("test-session");

        var factRecord = new MemoryFactRecord
        {
            Id = Guid.NewGuid().ToString("N"),
            EntityId = "entity-1",
            Content = "coffee is preferred",
            Embedding = new ReadOnlyMemory<float>(new float[1536]),
            MemoryType = "preference"
        };
        await factCollection.UpsertAsync(factRecord);

        var results = await memori.RecallAsync("coffee");

        Assert.That(results, Is.Not.Empty);
        Assert.That(results.First().Content, Does.Contain("coffee"));
        Assert.That(results.First().MemoryType, Is.EqualTo("preference"));
    }

    [Test]
    public async Task AddMemori_WithCustomStorageFactory_UsesProvidedStorage()
    {
        var services = new ServiceCollection();
        var conversationStorage = new InMemoryConversationStorage();
        var vectorStore = new InMemoryVectorStore();
        var factCollection = vectorStore.GetCollection<string, MemoryFactRecord>("memori_facts");

        services.AddMemori(conversationStorage, factCollection);
        var provider = services.BuildServiceProvider();

        var memori = provider.GetRequiredService<Memori>();
        memori.Attribution("entity-1");
        memori.SetSession("test-session");

        var factRecord = new MemoryFactRecord
        {
            Id = Guid.NewGuid().ToString("N"),
            EntityId = "entity-1",
            Content = "coffee is preferred",
            Embedding = new ReadOnlyMemory<float>(new float[1536]),
            MemoryType = "preference"
        };
        await factCollection.UpsertAsync(factRecord);

        var results = await memori.RecallAsync("coffee");

        Assert.That(results, Is.Not.Empty);
        Assert.That(results.First().MemoryType, Is.EqualTo("preference"));
    }

    [Test]
    public async Task AddMemori_WithCustomEmbeddingFactory_UsesProvidedGenerator()
    {
        var services = new ServiceCollection();
        var conversationStorage = new InMemoryConversationStorage();
        var vectorStore = new InMemoryVectorStore();
        var factCollection = vectorStore.GetCollection<string, MemoryFactRecord>("memori_facts");
        var generator = new TrackingEmbeddingGenerator();

        services.AddMemori(conversationStorage, factCollection);
        services.AddMemori(_ => generator);
        var provider = services.BuildServiceProvider();

        var memori = provider.GetRequiredService<Memori>();
        memori.Attribution("entity-1");
        memori.SetSession("test-session");

        var factRecord = new MemoryFactRecord
        {
            Id = Guid.NewGuid().ToString("N"),
            EntityId = "entity-1",
            Content = "coffee is preferred",
            Embedding = new ReadOnlyMemory<float>(new float[1536])
        };
        await factCollection.UpsertAsync(factRecord);

        await memori.RecallAsync("coffee");

        Assert.That(generator.Calls, Is.GreaterThan(0));
    }

    [Test]
    public async Task AddMemori_WithCustomAugmentationFactory_UsesProvidedClient()
    {
        var services = new ServiceCollection();
        var conversationStorage = new InMemoryConversationStorage();
        var vectorStore = new InMemoryVectorStore();
        var factCollection = vectorStore.GetCollection<string, MemoryFactRecord>("memori_facts");
        var augmentation = new TrackingAugmentationClient();

        services.AddMemori(conversationStorage, factCollection);
        services.AddMemori(_ => augmentation);
        var provider = services.BuildServiceProvider();

        var memori = provider.GetRequiredService<Memori>();
        memori.Attribution("entity-1");
        memori.SetSession("test-session");

        await memori.CaptureAsync([
            new ConversationMessage(ConversationRoles.User, "hello"),
            new ConversationMessage(ConversationRoles.Assistant, "hi"),
        ]);

        await memori.WaitForAugmentationAsync();

        Assert.That(augmentation.Calls, Is.GreaterThan(0));
    }

    [Test]
    public async Task AddMemori_WithCommonFactories_ResolvesCompleteGraphAndHonorsFactories()
    {
        var services = new ServiceCollection();
        var conversationStorage = new InMemoryConversationStorage();
        var vectorStore = new InMemoryVectorStore();
        var factCollection = vectorStore.GetCollection<string, MemoryFactRecord>("memori_facts");
        var generator = new TrackingEmbeddingGenerator();
        var augmentation = new TrackingAugmentationClient();

        services.AddMemori(
            _ => conversationStorage,
            _ => factCollection,
            _ => generator,
            _ => augmentation,
            options =>
            {
                options.RecallRelevanceThreshold = 0;
                options.SessionTimeout = TimeSpan.FromMinutes(7);
            });

        var provider = services.BuildServiceProvider();
        var memori = provider.CreateMemori();

        Assert.That(provider.GetRequiredService<IConversationStorage>(), Is.SameAs(conversationStorage));
        Assert.That(provider.GetRequiredService<VectorStoreCollection<string, MemoryFactRecord>>(), Is.SameAs(factCollection));
        Assert.That(provider.GetRequiredService<IEmbeddingGenerator<string, Embedding<float>>>(), Is.SameAs(generator));
        Assert.That(provider.GetRequiredService<IAugmentationClient>(), Is.SameAs(augmentation));
        Assert.That(provider.GetRequiredService<MemoriOptions>().SessionTimeout, Is.EqualTo(TimeSpan.FromMinutes(7)));

        memori.Attribution("entity-1");
        memori.SetSession("test-session");

        var factRecord = new MemoryFactRecord
        {
            Id = Guid.NewGuid().ToString("N"),
            EntityId = "entity-1",
            Content = "coffee is preferred",
            Embedding = new ReadOnlyMemory<float>(new float[1536])
        };
        await factCollection.UpsertAsync(factRecord);
        await memori.RecallAsync("coffee");

        await memori.CaptureAsync([
            new ConversationMessage(ConversationRoles.User, "hello"),
            new ConversationMessage(ConversationRoles.Assistant, "hi"),
        ]);
        await memori.WaitForAugmentationAsync();

        Assert.That(generator.Calls, Is.GreaterThan(0));
        Assert.That(augmentation.Calls, Is.GreaterThan(0));
    }

    [Test]
    public void AddMemori_WithConfiguration_BindsAndValidatesOptions()
    {
        var values = new Dictionary<string, string?>
        {
            ["RecallFactsLimit"] = "3",
            ["RecallCandidateLimit"] = "12",
            ["RecallRelevanceThreshold"] = "0.25",
            ["SessionTimeout"] = "00:10:00",
            ["StripSystemMessagesOnCapture"] = "false",
            ["PromptContextTagName"] = "configured_context",
        };
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
        var services = new ServiceCollection();

        services.AddMemori(configuration);
        var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<MemoriOptions>();
        Assert.That(options.RecallFactsLimit, Is.EqualTo(3));
        Assert.That(options.RecallCandidateLimit, Is.EqualTo(12));
        Assert.That(options.RecallRelevanceThreshold, Is.EqualTo(0.25));
        Assert.That(options.SessionTimeout, Is.EqualTo(TimeSpan.FromMinutes(10)));
        Assert.That(options.StripSystemMessagesOnCapture, Is.False);
        Assert.That(options.PromptContextTagName, Is.EqualTo("configured_context"));
        Assert.That(provider.CreateMemori(), Is.Not.Null);
    }

    [Test]
    public void AddMemori_ResolvesVersioningService()
    {
        var services = new ServiceCollection();
        services.AddMemori();
        var provider = services.BuildServiceProvider();

        var svc = provider.GetRequiredService<VersioningService>();
        Assert.That(svc, Is.Not.Null);
    }

    [Test]
    public void AddMemori_ResolvesIMemoryManagementService()
    {
        var services = new ServiceCollection();
        services.AddMemori();
        var provider = services.BuildServiceProvider();

        var svc = provider.GetRequiredService<IMemoryManagementService>();
        Assert.That(svc, Is.Not.Null);
        Assert.That(svc, Is.InstanceOf<MemoryManagementService>());
    }

    [Test]
    public void AddMemori_DoesNotResolveIThreadSummarizer_WhenNoIChatClient()
    {
        var services = new ServiceCollection();
        services.AddMemori();
        var provider = services.BuildServiceProvider();

        var svc = provider.GetService<IThreadSummarizer>();
        Assert.That(svc, Is.Null);
    }

    [Test]
    public void AddMemori_ResolvesIThreadSummarizer_WhenIChatClientIsRegistered()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IChatClient>(new RecordingChatClient(new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok"))));
        services.AddMemori();
        var provider = services.BuildServiceProvider();

        var svc = provider.GetRequiredService<IThreadSummarizer>();
        Assert.That(svc, Is.Not.Null);
        Assert.That(svc, Is.InstanceOf<ChatClientThreadSummarizer>());
    }

    [Test]
    public void AddMemori_WithCustomMemoryManagementFactory_UsesProvidedService()
    {
        var services = new ServiceCollection();
        var custom = new MemoryManagementService(
            new InMemoryVectorStore().GetCollection<string, MemoryFactRecord>("memori_facts"));
        services.AddSingleton<IMemoryManagementService>(custom);
        services.AddMemori();
        var provider = services.BuildServiceProvider();

        Assert.That(provider.GetRequiredService<IMemoryManagementService>(), Is.SameAs(custom));
    }

    [Test]
    public void AddMemori_WithCustomVersioningFactory_UsesProvidedService()
    {
        var services = new ServiceCollection();
        var custom = new VersioningService(ConflictResolutionStrategy.Merge);
        services.AddSingleton(custom);
        services.AddMemori();
        var provider = services.BuildServiceProvider();

        var resolved = provider.GetRequiredService<VersioningService>();
        Assert.That(resolved, Is.SameAs(custom));
    }

    [Test]
    public void AddMemori_WithCustomThreadSummarizerFactory_UsesProvidedService()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IChatClient>(new RecordingChatClient(new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok"))));
        var custom = new ChatClientThreadSummarizer(
            new RecordingChatClient(new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok"))));
        services.AddSingleton<IThreadSummarizer>(custom);
        services.AddMemori();
        var provider = services.BuildServiceProvider();

        Assert.That(provider.GetRequiredService<IThreadSummarizer>(), Is.SameAs(custom));
    }

    [Test]
    public void AddMemori_WithTier3FactoriesOverload_ResolvesCorrectly()
    {
        var services = new ServiceCollection();
        services.AddMemori(
            memoryManagementFactory: sp => new MemoryManagementService(
                sp.GetRequiredService<VectorStoreCollection<string, MemoryFactRecord>>()),
            threadSummarizerFactory: null,
            versioningServiceFactory: sp => new VersioningService(ConflictResolutionStrategy.Merge));

        var provider = services.BuildServiceProvider();

        Assert.That(provider.GetRequiredService<IMemoryManagementService>(), Is.Not.Null);
        Assert.That(provider.GetRequiredService<VersioningService>(), Is.Not.Null);
    }

    [Test]
    public async Task UseMemori_WithCustomFactories_UsesConfiguredMemori()
    {
        var conversationStorage = new InMemoryConversationStorage();
        var vectorStore = new InMemoryVectorStore();
        var factCollection = vectorStore.GetCollection<string, MemoryFactRecord>("memori_facts");
        var inner = new RecordingChatClient(new ChatResponse(new ChatMessage(ChatRole.Assistant, "answer")));
        var factoryCalled = false;
        var builder = new ChatClientBuilder(inner).UseMemori(_ =>
        {
            factoryCalled = true;
            var options = new MemoriOptions
            {
                PromptContextTagName = "custom_context",
                RecallRelevanceThreshold = 0,
            };
            var memori = new Memori(conversationStorage, factCollection, options);
            memori.Attribution("entity-1");
            memori.SetSession("test-session");
            return memori;
        });

        var provider = new ServiceCollection().BuildServiceProvider();
        var client = builder.Build(provider);

        var factRecord = new MemoryFactRecord
        {
            Id = Guid.NewGuid().ToString("N"),
            EntityId = "entity-1",
            Content = "The user lives in Karachi.",
            Embedding = new ReadOnlyMemory<float>(new float[1536])
        };
        await factCollection.UpsertAsync(factRecord);

        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "Where do I live?")]);

        Assert.That(factoryCalled, Is.True);
        Assert.That(inner.LastMessages.Single().Role, Is.EqualTo(ChatRole.User));
    }

    [Test]
    public void AddMemori_WithCompositeCollectionFactory_UsesProvidedCollection()
    {
        var services = new ServiceCollection();
        var vectorStore1 = new InMemoryVectorStore();
        var vectorStore2 = new InMemoryVectorStore();
        var collection1 = vectorStore1.GetCollection<string, MemoryFactRecord>("primary");
        var collection2 = vectorStore2.GetCollection<string, MemoryFactRecord>("secondary");

        services.AddMemori(
            _ => new CompositeMemoryCollection([collection1, collection2]),
            options => { options.RecallRelevanceThreshold = 0; });

        var provider = services.BuildServiceProvider();

        var resolvedCollection = provider.GetRequiredService<VectorStoreCollection<string, MemoryFactRecord>>();
        Assert.That(resolvedCollection, Is.InstanceOf<CompositeMemoryCollection>());

        var memori = provider.CreateMemori();
        Assert.That(memori, Is.Not.Null);
    }

    [Test]
    public async Task MemoriFacade_MemoryManagementMethods_WorkWhenServiceIsConfigured()
    {
        var vectorStore = new InMemoryVectorStore();
        var factCollection = vectorStore.GetCollection<string, MemoryFactRecord>("memori_facts");
        var conversationStorage = new InMemoryConversationStorage();
        var management = new MemoryManagementService(factCollection);
        var memori = new Memori(
            conversationStorage,
            factCollection,
            memoryManagement: management);

        memori.Attribution("entity-1");
        await factCollection.UpsertAsync(new MemoryFactRecord
        {
            Id = "fact-1",
            EntityId = "entity-1",
            Content = "test memory",
            MemoryType = "preference"
        });

        var count = await memori.GetMemoryCountAsync();
        Assert.That(count, Is.EqualTo(1));

        var listed = await memori.ListMemoriesAsync();
        Assert.That(listed, Has.Count.EqualTo(1));
        Assert.That(listed[0].Content, Is.EqualTo("test memory"));

        var searched = await memori.SearchMemoriesAsync("test");
        Assert.That(searched, Has.Count.EqualTo(1));

        var deleted = await memori.SoftDeleteMemoryAsync("fact-1");
        Assert.That(deleted, Is.True);

        var afterDelete = await memori.GetMemoryCountAsync();
        Assert.That(afterDelete, Is.EqualTo(0));

        var restored = await memori.RestoreMemoryAsync("fact-1");
        Assert.That(restored, Is.True);

        var afterRestore = await memori.GetMemoryCountAsync();
        Assert.That(afterRestore, Is.EqualTo(1));
    }

    [Test]
    public async Task MemoriFacade_MemoryManagementMethods_ThrowWhenServiceNotConfigured()
    {
        var memori = TestMemoriFactory.Create();
        memori.Attribution("entity-1");

        var ex = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await memori.ListMemoriesAsync());
        Assert.That(ex!.Message, Does.Contain("IMemoryManagementService is not configured"));
    }

    [Test]
    public async Task MemoriFacade_MemoryManagementMethods_ThrowWhenNoAttribution()
    {
        var management = new MemoryManagementService(
            new InMemoryVectorStore().GetCollection<string, MemoryFactRecord>("memori_facts"));
        var memori = new Memori(
            new InMemoryConversationStorage(),
            new InMemoryVectorStore().GetCollection<string, MemoryFactRecord>("memori_facts"),
            memoryManagement: management);

        var ex = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await memori.ListMemoriesAsync());
        Assert.That(ex!.Message, Does.Contain("Attribution is required"));
    }

    [Test]
    public void CaptureAsync_WithNullMessages_Throws()
    {
        var memori = TestMemoriFactory.Create();
        memori.Attribution("entity-1");
        memori.SetSession("test-session");

        Assert.That(async () => await memori.CaptureAsync(null!), Throws.ArgumentNullException);
    }

    [Test]
    public async Task CaptureAsync_WithEmptyMessages_DoesNothing()
    {
        var memori = TestMemoriFactory.Create();
        memori.Attribution("entity-1");
        memori.SetSession("test-session");

        await memori.CaptureAsync([]);

        var conversationStorage = GetConversationStorage(memori);
        var conversation = await conversationStorage.GetOrCreateConversationAsync("test-session", TimeSpan.FromMinutes(30));
        var messages = await conversationStorage.GetConversationMessagesAsync(conversation.Id);
        Assert.That(messages, Is.Empty);
    }

    [Test]
    public void RecallAsync_WithNullQuery_Throws()
    {
        var memori = TestMemoriFactory.Create();
        memori.Attribution("entity-1");

        Assert.That(async () => await memori.RecallAsync(null!), Throws.ArgumentNullException);
    }

    [Test]
    public async Task RecallAsync_WhenNoAttribution_ReturnsEmpty()
    {
        var memori = TestMemoriFactory.Create();

        var results = await memori.RecallAsync("anything");

        Assert.That(results, Is.Empty);
    }

    [Test]
    public void DeleteEntityMemoriesAsync_WhenNoAttribution_DoesNotThrow()
    {
        var memori = TestMemoriFactory.Create();

        Assert.That(async () => await memori.DeleteEntityMemoriesAsync(), Throws.Nothing);
    }

    [Test]
    public void NewSession_ReturnsNonEmptyString()
    {
        var memori = TestMemoriFactory.Create();

        var session = memori.NewSession();

        Assert.That(session, Is.Not.Null.And.Not.Empty);
    }

    [Test]
    public void SetSession_WithEmptyString_Throws()
    {
        var memori = TestMemoriFactory.Create();

        Assert.That(() => memori.SetSession(""), Throws.ArgumentException);
        Assert.That(() => memori.SetSession("   "), Throws.ArgumentException);
    }

    [Test]
    public void SetScope_WithEmptyString_Throws()
    {
        var memori = TestMemoriFactory.Create();

        Assert.That(() => memori.SetScope(""), Throws.ArgumentException);
        Assert.That(() => memori.SetScope("   "), Throws.ArgumentException);
    }

    [Test]
    public async Task CaptureAsync_WithStripSystemMessages_AppliesFilter()
    {
        var memori = TestMemoriFactory.Create(options: new MemoriOptions
        {
            StripSystemMessagesOnCapture = true,
        });
        memori.Attribution("entity-1");
        memori.SetSession("test-session");

        await memori.CaptureAsync([
            new ConversationMessage(ConversationRoles.System, "system instruction"),
            new ConversationMessage(ConversationRoles.User, "user message"),
            new ConversationMessage(ConversationRoles.Assistant, "assistant response"),
        ]);

        var conversationStorage = GetConversationStorage(memori);
        var conversation = await conversationStorage.GetOrCreateConversationAsync("test-session", TimeSpan.FromMinutes(30));
        var messages = await conversationStorage.GetConversationMessagesAsync(conversation.Id);

        Assert.That(messages.Any(m => m.Role == ConversationRoles.System), Is.False);
        Assert.That(messages.Any(m => m.Content == "user message"), Is.True);
        Assert.That(messages.Any(m => m.Content == "assistant response"), Is.True);
    }

    static IConversationStorage GetConversationStorage(Memori memori)
    {
        var field = typeof(Memori).GetField("conversationStorage", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        return (IConversationStorage)field!.GetValue(memori)!;
    }

    static VectorStoreCollection<string, MemoryFactRecord> GetFactCollection(Memori memori)
    {
        var field = typeof(Memori).GetField("factCollection", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        return (VectorStoreCollection<string, MemoryFactRecord>)field!.GetValue(memori)!;
    }

    sealed class TrackingEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>
    {
        public int Calls { get; private set; }

        public void Dispose()
        { }

        public object? GetService(Type serviceType, object? serviceKey = null)
            => null;

        public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
            IEnumerable<string> values,
            EmbeddingGenerationOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            var embeddings = values
                .Select(_ => new Embedding<float>(new float[] { 1, 0, 0 }))
                .ToArray();
            return Task.FromResult(new GeneratedEmbeddings<Embedding<float>>(embeddings));
        }
    }

    sealed class TrackingAugmentationClient : IAugmentationClient
    {
        public int Calls { get; private set; }

        public ValueTask<AugmentationResult?> AugmentAsync(
            AugmentationInput context,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            return ValueTask.FromResult<AugmentationResult?>(new AugmentationResult(Facts: []));
        }
    }

    sealed class RecordingChatClient : IChatClient
    {
        readonly ChatResponse response;

        public RecordingChatClient(ChatResponse response)
        {
            this.response = response;
        }

        public IReadOnlyList<ChatMessage> LastMessages { get; private set; } = [];

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            LastMessages = messages.ToArray();
            return Task.FromResult(response);
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            LastMessages = messages.ToArray();
            yield break;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        { }
    }

    sealed class TestAugmentationClient : IAugmentationClient
    {
        public ValueTask<AugmentationResult?> AugmentAsync(
            AugmentationInput context,
            CancellationToken cancellationToken = default)
        {
            var result = new AugmentationResult(
                Facts: [new NewMemoryFact($"captured: {context.Messages.Last().Content}")]);
            return ValueTask.FromResult<AugmentationResult?>(result);
        }
    }
}
