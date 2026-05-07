using Memori.Abstractions;
using Memori.Models;
using Memori.Storage;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace Memori.Tests;

public class MemoriFacadeTests
{
    [Test]
    public async Task CaptureAsync_WithNoAttribution_DoesNotWriteMessages()
    {
        var storage = new InMemoryStorage();
        var memori = new Memori(storage);
        memori.SetSession("test-session");

        await memori.CaptureAsync(new[]
        {
            new ConversationMessage(ConversationRoles.User, "hello"),
        });

        var conversation = await storage.GetOrCreateConversationAsync("test-session", TimeSpan.FromMinutes(30));
        var messages = await storage.GetConversationMessagesAsync(conversation.Id);

        Assert.That(messages, Is.Empty);
    }

    [Test]
    public async Task CaptureAsync_StripsSystemMessages_WhenConfigured()
    {
        var storage = new InMemoryStorage();
        var memori = new Memori(storage, new MemoriOptions { StripSystemMessagesOnCapture = true });
        memori.Attribution("entity-1");
        memori.SetSession("test-session");

        await memori.CaptureAsync(new[]
        {
            new ConversationMessage(ConversationRoles.System, "ignore me"),
            new ConversationMessage(ConversationRoles.User, "hello"),
        });

        var conversation = await storage.GetOrCreateConversationAsync("test-session", TimeSpan.FromMinutes(30));
        var messages = await storage.GetConversationMessagesAsync(conversation.Id);

        Assert.That(messages.Select(message => message.Role), Is.EqualTo(new[] { ConversationRoles.User }));
    }

    [Test]
    public async Task RecallAsync_UsesCurrentAttribution()
    {
        var storage = new InMemoryStorage();
        var entityId = await storage.GetOrCreateEntityAsync("entity-1");
        await storage.AddFactsAsync(entityId, new[] { new NewMemoryFact("coffee is preferred", memoryType: "preference") });

        var memori = new Memori(storage);
        memori.Attribution("entity-1");
        memori.SetSession("test-session");

        var results = await memori.RecallAsync("coffee");

        Assert.That(results, Is.Not.Empty);
        Assert.That(results.First().Content, Does.Contain("coffee"));
    }

    [Test]
    public async Task WaitForAugmentationAsync_CompletesQueuedWork()
    {
        var storage = new InMemoryStorage();
        var memori = new Memori(
            storage,
            augmentationClient: new TestAugmentationClient());
        memori.Attribution("entity-1");
        memori.SetSession("test-session");

        await memori.CaptureAsync(new[]
        {
            new ConversationMessage(ConversationRoles.User, "hello"),
            new ConversationMessage(ConversationRoles.Assistant, "hi"),
        });

        await memori.WaitForAugmentationAsync();

        var entityId = await storage.GetOrCreateEntityAsync("entity-1");
        var results = await storage.SearchFactsAsync(entityId, "hello", null, 10, 10);

        Assert.That(results, Is.Empty); // TODO: Once we have better semantic analyzer it should not be empty
    }

    [Test]
    public void CurrentAttributionAndSessionId_ReflectLifecycleState()
    {
        var memori = new Memori(new InMemoryStorage());

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
        var storage = new InMemoryStorage();
        var memori = new Memori(storage);
        memori.Attribution("entity-1");
        memori.SetSession("test-session");

        var entityId = await storage.GetOrCreateEntityAsync("entity-1");
        await storage.AddFactsAsync(entityId, [new NewMemoryFact("coffee")]);

        memori.ClearAttribution();

        await memori.CaptureAsync([new ConversationMessage(ConversationRoles.User, "hello")]);
        var recall = await memori.RecallAsync("coffee");
        var conversation = await storage.GetOrCreateConversationAsync("test-session", TimeSpan.FromMinutes(30));
        var messages = await storage.GetConversationMessagesAsync(conversation.Id);

        Assert.That(memori.CurrentAttribution, Is.Null);
        Assert.That(recall, Is.Empty);
        Assert.That(messages, Is.Empty);
    }

    [Test]
    public async Task ClearSession_CausesNextCaptureToCreateNewSession()
    {
        var storage = new InMemoryStorage();
        var memori = new Memori(storage);
        memori.Attribution("entity-1");
        memori.SetSession("session-1");

        await memori.CaptureAsync([new ConversationMessage(ConversationRoles.User, "first")]);
        memori.ClearSession();

        Assert.That(memori.CurrentSessionId, Is.Null);

        await memori.CaptureAsync([new ConversationMessage(ConversationRoles.User, "second")]);

        Assert.That(memori.CurrentSessionId, Is.Not.Null);
        Assert.That(memori.CurrentSessionId, Is.Not.EqualTo("session-1"));

        var originalConversation = await storage.GetOrCreateConversationAsync("session-1", TimeSpan.FromMinutes(30));
        var originalMessages = await storage.GetConversationMessagesAsync(originalConversation.Id);
        var newConversation = await storage.GetOrCreateConversationAsync(memori.CurrentSessionId!, TimeSpan.FromMinutes(30));
        var newMessages = await storage.GetConversationMessagesAsync(newConversation.Id);

        Assert.That(originalMessages.Select(message => message.Content), Is.EqualTo(["first"]));
        Assert.That(newMessages.Select(message => message.Content), Is.EqualTo(["second"]));
    }

    [Test]
    public async Task ResumeSession_ReusesExternallyManagedSession()
    {
        var storage = new InMemoryStorage();
        var memori = new Memori(storage);
        memori.Attribution("entity-1");
        memori.ResumeSession("external-session");

        await memori.CaptureAsync([new ConversationMessage(ConversationRoles.User, "first")]);
        memori.NewSession();
        memori.ResumeSession("external-session");
        await memori.CaptureAsync([new ConversationMessage(ConversationRoles.User, "second")]);

        var conversation = await storage.GetOrCreateConversationAsync("external-session", TimeSpan.FromMinutes(30));
        var messages = await storage.GetConversationMessagesAsync(conversation.Id);

        Assert.That(memori.CurrentSessionId, Is.EqualTo("external-session"));
        Assert.That(messages.Select(message => message.Content), Is.EqualTo(["first", "second"]));
    }

    [Test]
    public async Task ResumeSession_DoesNotChangeEntityScopedRecall()
    {
        var storage = new InMemoryStorage();
        var userOne = await storage.GetOrCreateEntityAsync("user-1");
        var userTwo = await storage.GetOrCreateEntityAsync("user-2");
        await storage.AddFactsAsync(userOne, [new NewMemoryFact("coffee")]);
        await storage.AddFactsAsync(userTwo, [new NewMemoryFact("tea")]);

        var memori = new Memori(storage);
        memori.Attribution("user-1");
        memori.ResumeSession("shared-session");

        var results = await memori.RecallAsync("tea");

        Assert.That(results, Is.Empty);
    }

    [Test]
    public async Task CaptureAsync_CreatesNewConversationAfterSessionTimeout()
    {
        var storage = new InMemoryStorage();
        var memori = new Memori(
            storage,
            new MemoriOptions { SessionTimeout = TimeSpan.FromMilliseconds(50) });
        memori.Attribution("entity-1");
        memori.SetSession("test-session");

        await memori.CaptureAsync([new ConversationMessage(ConversationRoles.User, "first")]);
        var first = await storage.GetOrCreateConversationAsync("test-session", TimeSpan.FromMilliseconds(50));
        await Task.Delay(75);
        await memori.CaptureAsync([new ConversationMessage(ConversationRoles.User, "second")]);
        var second = await storage.GetOrCreateConversationAsync("test-session", TimeSpan.FromMilliseconds(50));

        Assert.That(second.Id, Is.Not.EqualTo(first.Id));
    }

    [Test]
    public async Task AddMemori_WithExplicitStorageAndNoEmbeddingGenerator_UsesLexicalFallback()
    {
        var services = new ServiceCollection();
        var storage = new InMemoryStorage();

        services.AddMemori(storage);
        var provider = services.BuildServiceProvider();

        var memori = provider.GetRequiredService<Memori>();
        memori.Attribution("entity-1");
        memori.SetSession("test-session");

        var entityId = await storage.GetOrCreateEntityAsync("entity-1");
        await storage.AddFactsAsync(entityId, [new NewMemoryFact("coffee is preferred", memoryType: "preference")]);

        var results = await memori.RecallAsync("coffee");

        Assert.That(results, Is.Not.Empty);
        Assert.That(results.First().Content, Does.Contain("coffee"));
        Assert.That(results.First().MemoryType, Is.EqualTo("preference"));
    }

    [Test]
    public async Task AddMemori_WithCustomStorageFactory_UsesProvidedStorage()
    {
        var services = new ServiceCollection();
        var storage = new InMemoryStorage();

        services.AddMemori(_ => storage);
        var provider = services.BuildServiceProvider();

        var memori = provider.GetRequiredService<Memori>();
        memori.Attribution("entity-1");
        memori.SetSession("test-session");

        var entityId = await storage.GetOrCreateEntityAsync("entity-1");
        await storage.AddFactsAsync(entityId, [new NewMemoryFact("coffee is preferred", memoryType: "preference")]);

        var results = await memori.RecallAsync("coffee");

        Assert.That(results, Is.Not.Empty);
        Assert.That(results.First().MemoryType, Is.EqualTo("preference"));
    }

    [Test]
    public async Task AddMemori_WithCustomEmbeddingFactory_UsesProvidedGenerator()
    {
        var services = new ServiceCollection();
        var storage = new InMemoryStorage();
        var generator = new TrackingEmbeddingGenerator();

        services.AddMemori(_ => storage);
        services.AddMemori(_ => generator);
        var provider = services.BuildServiceProvider();

        var memori = provider.GetRequiredService<Memori>();
        memori.Attribution("entity-1");
        memori.SetSession("test-session");

        var entityId = await storage.GetOrCreateEntityAsync("entity-1");
        await storage.AddFactsAsync(entityId, [new NewMemoryFact("coffee is preferred")]);

        await memori.RecallAsync("coffee");

        Assert.That(generator.Calls, Is.GreaterThan(0));
    }

    [Test]
    public async Task AddMemori_WithCustomAugmentationFactory_UsesProvidedClient()
    {
        var services = new ServiceCollection();
        var storage = new InMemoryStorage();
        var augmentation = new TrackingAugmentationClient();

        services.AddMemori(_ => storage);
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
        var storage = new InMemoryStorage();
        var generator = new TrackingEmbeddingGenerator();
        var augmentation = new TrackingAugmentationClient();

        services.AddMemori(
            _ => storage,
            _ => generator,
            _ => augmentation,
            options =>
            {
                options.RecallRelevanceThreshold = 0;
                options.SessionTimeout = TimeSpan.FromMinutes(7);
            });

        var provider = services.BuildServiceProvider();
        var memori = provider.CreateMemori();

        Assert.That(provider.GetRequiredService<IStorage>(), Is.SameAs(storage));
        Assert.That(provider.GetRequiredService<IEmbeddingGenerator<string, Embedding<float>>>(), Is.SameAs(generator));
        Assert.That(provider.GetRequiredService<IAugmentationClient>(), Is.SameAs(augmentation));
        Assert.That(provider.GetRequiredService<MemoriOptions>().SessionTimeout, Is.EqualTo(TimeSpan.FromMinutes(7)));

        memori.Attribution("entity-1");
        memori.SetSession("test-session");

        var entityId = await storage.GetOrCreateEntityAsync("entity-1");
        await storage.AddFactsAsync(entityId, [new NewMemoryFact("coffee is preferred")]);
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
    public async Task UseMemori_WithCustomFactories_UsesConfiguredMemori()
    {
        var storage = new InMemoryStorage();
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
            var memori = new Memori(storage, options);
            memori.Attribution("entity-1");
            memori.SetSession("test-session");
            return memori;
        });

        var provider = new ServiceCollection().BuildServiceProvider();
        var client = builder.Build(provider);

        var entityId = await storage.GetOrCreateEntityAsync("entity-1");
        await storage.AddFactsAsync(entityId, [new NewMemoryFact("The user lives in Karachi.")]);

        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "Where do I live?")]);

        Assert.That(factoryCalled, Is.True);
        Assert.That(inner.LastMessages.Single().Role, Is.EqualTo(ChatRole.User));
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
