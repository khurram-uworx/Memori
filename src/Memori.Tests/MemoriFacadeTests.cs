using Memori.Abstractions;
using Memori.Embeddings;
using Memori.Models;
using Memori.Storage;
using Microsoft.Extensions.AI;
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
