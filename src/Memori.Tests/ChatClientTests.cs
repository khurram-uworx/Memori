using Memori.Abstractions;
using Memori.Models;
using Memori.Storage;
using Microsoft.Extensions.AI;
using NUnit.Framework;

namespace Memori.Tests;

public class ChatClientTests
{
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

    sealed class StreamingRecordingChatClient : IChatClient
    {
        readonly IReadOnlyList<ChatResponseUpdate> updates;

        public StreamingRecordingChatClient(IReadOnlyList<ChatResponseUpdate> updates)
        {
            this.updates = updates;
        }

        public IReadOnlyList<ChatMessage> LastMessages { get; private set; } = [];

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "streamed answer")));

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            LastMessages = messages.ToArray();
            foreach (var update in updates)
            {
                yield return update;
            }
            await Task.CompletedTask;
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
            return ValueTask.FromResult<AugmentationResult?>(
                new AugmentationResult(Facts: [new NewMemoryFact("assistant said answer")]));
        }
    }

    sealed class CancellableStreamingChatClient : IChatClient
    {
        readonly IReadOnlyList<ChatResponseUpdate> updates;
        readonly CancellationTokenSource cts;

        public CancellableStreamingChatClient(IReadOnlyList<ChatResponseUpdate> updates, CancellationTokenSource cts)
        {
            this.updates = updates;
            this.cts = cts;
        }

        public IReadOnlyList<ChatMessage> LastMessages { get; private set; } = [];

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "response")));

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            LastMessages = messages.ToArray();
            foreach (var update in updates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return update;
            }
            await Task.CompletedTask;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        { }
    }

    [Test]
    public async Task GetResponseAsync_UsesSharedPromptFormatter()
    {
        var storage = new InMemoryStorage();
        var options = new MemoriOptions
        {
            PromptContextTagName = "custom_context",
            PromptContextInstruction = "Follow the custom instruction.",
            PromptFactsHeading = "Custom facts:",
            IncludeFactTimestampsInPrompt = true,
            RecallRelevanceThreshold = 0,
        };
        var memori = new Memori(storage, options);
        memori.Attribution("entity-1");
        memori.SetSession("test-session");

        var entityId = await storage.GetOrCreateEntityAsync("entity-1");
        await storage.AddFactsAsync(
            entityId,
            [
                new NewMemoryFact(
                    "The user lives in Karachi.",
                    summaries: [new MemorySummary("Lives in Karachi.", new DateTimeOffset(2026, 5, 6, 12, 30, 0, TimeSpan.Zero))],
                    createdAt: new DateTimeOffset(2026, 5, 6, 12, 0, 0, TimeSpan.Zero))
            ]);

        var inner = new RecordingChatClient(new ChatResponse(new ChatMessage(ChatRole.Assistant, "answer")));
        var client = new MemoriChatClient(inner, memori);

        await client.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "Tell me about Karachi.")]);

        var injected = inner.LastMessages.Single(message => message.Role == ChatRole.System).Text ?? string.Empty;
        Assert.That(injected, Does.StartWith("<custom_context>"));
        Assert.That(injected, Does.Contain("Follow the custom instruction."));
        Assert.That(injected, Does.Contain("Custom facts:"));
        Assert.That(injected, Does.Contain("The user lives in Karachi."));
        Assert.That(injected, Does.Contain("Stated at 2026-05-06 12:00:00"));
        Assert.That(injected, Does.Contain("## Summaries"));
        Assert.That(injected, Does.Contain("[2026-05-06 12:30:00] Lives in Karachi."));
        Assert.That(injected, Does.Contain("Lives in Karachi."));
        Assert.That(injected, Does.EndWith("</custom_context>"));
    }

    [Test]
    public async Task GetResponseAsync_DoesNotCaptureInjectedMemoryContext()
    {
        var storage = new InMemoryStorage();
        var options = new MemoriOptions
        {
            StripSystemMessagesOnCapture = false,
            RecallRelevanceThreshold = 0,
        };
        var memori = new Memori(storage, options);
        memori.Attribution("entity-1");
        memori.SetSession("test-session");

        var entityId = await storage.GetOrCreateEntityAsync("entity-1");
        await storage.AddFactsAsync(
            entityId,
            [new NewMemoryFact("The user lives in Karachi.")]);

        var inner = new RecordingChatClient(new ChatResponse(new ChatMessage(ChatRole.Assistant, "answer")));
        var client = new MemoriChatClient(inner, memori);

        await client.GetResponseAsync(
            [
                new ChatMessage(ChatRole.System, "Host system instruction."),
                new ChatMessage(ChatRole.User, "Where do I live?"),
            ]);

        await memori.WaitForAugmentationAsync();

        var conversation = await storage.GetOrCreateConversationAsync("test-session", TimeSpan.FromMinutes(30));
        var messages = await storage.GetConversationMessagesAsync(conversation.Id);

        Assert.That(messages.Count, Is.EqualTo(3));
        Assert.That(messages[0].Role, Is.EqualTo(ConversationRoles.System));
        Assert.That(messages[0].Content, Is.EqualTo("Host system instruction."));
        Assert.That(messages.Any(message => message.Content.Contains("memori_context", StringComparison.OrdinalIgnoreCase)), Is.False);
        Assert.That(messages.Any(message => message.Content.Contains("The user lives in Karachi.", StringComparison.OrdinalIgnoreCase)), Is.False);
        Assert.That(messages.Any(message => message.Role == ConversationRoles.Assistant && message.Content == "answer"), Is.True);
    }

    [Test]
    public async Task GetResponseAsync_InjectsMemoryContextAndCapturesResponse()
    {
        var storage = new InMemoryStorage();
        var memori = new Memori(storage, augmentationClient: new TestAugmentationClient());
        memori.Attribution("entity-1");
        memori.SetSession("test-session");

        var inner = new RecordingChatClient(new ChatResponse(new ChatMessage(ChatRole.Assistant, "answer")));
        var client = new MemoriChatClient(inner, memori);

        var response = await client.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "What do you remember about me?")]);

        Assert.That(response.Messages.Single().Text, Is.EqualTo("answer"));
        //TODO: Fix/Check this Assert.That(inner.LastMessages.Any(message => message.Role == ChatRole.Assistant), Is.True);

        await memori.WaitForAugmentationAsync();
        var entityId = await storage.GetOrCreateEntityAsync("entity-1");
        var facts = await storage.SearchFactsAsync(entityId, "answer", null, 10, 10);
        Assert.That(facts, Is.Not.Empty);
    }

    [Test]
    public async Task GetStreamingResponseAsync_CapturesFinalAssistantMessage()
    {
        var storage = new InMemoryStorage();
        var memori = new Memori(storage);
        memori.Attribution("entity-1");
        memori.SetSession("test-session");

        var inner = new StreamingRecordingChatClient(
            [new ChatResponseUpdate(ChatRole.Assistant, "streamed answer")]);
        var client = new MemoriChatClient(inner, memori);

        var updates = new List<ChatResponseUpdate>();
        await foreach (var update in client.GetStreamingResponseAsync(
            [new ChatMessage(ChatRole.User, "Tell me something")]))
        {
            updates.Add(update);
        }

        Assert.That(updates, Is.Not.Empty);
        var conversation = await storage.GetOrCreateConversationAsync("test-session", TimeSpan.FromMinutes(30));
        var messages = await storage.GetConversationMessagesAsync(conversation.Id);
        Assert.That(messages.Any(message => message.Role == ConversationRoles.Assistant), Is.True);
    }

    [Test]
    public async Task GetResponseAsync_SkipsRecallWhenDisabled()
    {
        var storage = new InMemoryStorage();
        var memori = new Memori(storage);
        memori.Attribution("entity-1");
        memori.SetSession("test-session");

        var entityId = await storage.GetOrCreateEntityAsync("entity-1");
        await storage.AddFactsAsync(
            entityId,
            [new NewMemoryFact("The user lives in Karachi.")]);

        var inner = new RecordingChatClient(new ChatResponse(new ChatMessage(ChatRole.Assistant, "answer")));
        var client = new MemoriChatClient(inner, memori);

        var requestOptions = new MemoriRequestOptions { EnableRecall = false };
        var options = new ChatOptions();
        options.AdditionalProperties ??= new AdditionalPropertiesDictionary();
        options.AdditionalProperties[MemoriChatClient.MemoriRequestOptionsKey] = requestOptions;

        await client.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "Tell me about Karachi.")],
            options);

        // Verify no system message was injected
        var systemMessages = inner.LastMessages.Where(m => m.Role == ChatRole.System).ToList();
        Assert.That(systemMessages, Is.Empty);
    }

    [Test]
    public async Task GetResponseAsync_SkipsCaptureWhenDisabled()
    {
        var storage = new InMemoryStorage();
        var memori = new Memori(storage);
        memori.Attribution("entity-1");
        memori.SetSession("test-session");

        var inner = new RecordingChatClient(new ChatResponse(new ChatMessage(ChatRole.Assistant, "answer")));
        var client = new MemoriChatClient(inner, memori);

        var requestOptions = new MemoriRequestOptions { EnableCapture = false };
        var options = new ChatOptions();
        options.AdditionalProperties ??= new AdditionalPropertiesDictionary();
        options.AdditionalProperties[MemoriChatClient.MemoriRequestOptionsKey] = requestOptions;

        await client.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "Hello")],
            options);

        // Verify no messages were captured
        var conversation = await storage.GetOrCreateConversationAsync("test-session", TimeSpan.FromMinutes(30));
        var messages = await storage.GetConversationMessagesAsync(conversation.Id);

        Assert.That(messages, Is.Empty);
    }

    [Test]
    public async Task GetResponseAsync_CaptureOnlyMode()
    {
        var storage = new InMemoryStorage();
        var memori = new Memori(storage);
        memori.Attribution("entity-1");
        memori.SetSession("test-session");

        var inner = new RecordingChatClient(new ChatResponse(new ChatMessage(ChatRole.Assistant, "answer")));
        var client = new MemoriChatClient(inner, memori);

        var requestOptions = new MemoriRequestOptions
        {
            EnableRecall = false,
            EnableCapture = true
        };
        var options = new ChatOptions();
        options.AdditionalProperties ??= new AdditionalPropertiesDictionary();
        options.AdditionalProperties[MemoriChatClient.MemoriRequestOptionsKey] = requestOptions;

        await client.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "Hello")],
            options);

        // Verify recall did not happen (no system message)
        var systemMessages = inner.LastMessages.Where(m => m.Role == ChatRole.System).ToList();
        Assert.That(systemMessages, Is.Empty);

        // Verify capture happened
        var conversation = await storage.GetOrCreateConversationAsync("test-session", TimeSpan.FromMinutes(30));
        var messages = await storage.GetConversationMessagesAsync(conversation.Id);
        Assert.That(messages, Is.Not.Empty);
        Assert.That(messages.Any(m => m.Content == "Hello"), Is.True);
        Assert.That(messages.Any(m => m.Content == "answer"), Is.True);
    }

    [Test]
    public async Task GetStreamingResponseAsync_SkipsRecallWhenDisabled()
    {
        var storage = new InMemoryStorage();
        var memori = new Memori(storage);
        memori.Attribution("entity-1");
        memori.SetSession("test-session");

        var entityId = await storage.GetOrCreateEntityAsync("entity-1");
        await storage.AddFactsAsync(
            entityId,
            [new NewMemoryFact("The user lives in Karachi.")]);

        var inner = new StreamingRecordingChatClient(
            [new ChatResponseUpdate(ChatRole.Assistant, "streamed answer")]);
        var client = new MemoriChatClient(inner, memori);

        var requestOptions = new MemoriRequestOptions { EnableRecall = false };
        var options = new ChatOptions();
        options.AdditionalProperties ??= new AdditionalPropertiesDictionary();
        options.AdditionalProperties[MemoriChatClient.MemoriRequestOptionsKey] = requestOptions;

        var updates = new List<ChatResponseUpdate>();
        await foreach (var update in client.GetStreamingResponseAsync(
            [new ChatMessage(ChatRole.User, "Tell me about Karachi.")],
            options))
        {
            updates.Add(update);
        }

        // Verify no system message was injected
        var systemMessages = inner.LastMessages.Where(m => m.Role == ChatRole.System).ToList();
        Assert.That(systemMessages, Is.Empty);
    }

    [Test]
    public async Task GetStreamingResponseAsync_SkipsCaptureWhenDisabled()
    {
        var storage = new InMemoryStorage();
        var memori = new Memori(storage);
        memori.Attribution("entity-1");
        memori.SetSession("test-session");

        var inner = new StreamingRecordingChatClient(
            [new ChatResponseUpdate(ChatRole.Assistant, "streamed answer")]);
        var client = new MemoriChatClient(inner, memori);

        var requestOptions = new MemoriRequestOptions { EnableCapture = false };
        var options = new ChatOptions();
        options.AdditionalProperties ??= new AdditionalPropertiesDictionary();
        options.AdditionalProperties[MemoriChatClient.MemoriRequestOptionsKey] = requestOptions;

        var updates = new List<ChatResponseUpdate>();
        await foreach (var update in client.GetStreamingResponseAsync(
            [new ChatMessage(ChatRole.User, "Hello")],
            options))
        {
            updates.Add(update);
        }

        // Verify no messages were captured
        var conversation = await storage.GetOrCreateConversationAsync("test-session", TimeSpan.FromMinutes(30));
        var messages = await storage.GetConversationMessagesAsync(conversation.Id);

        Assert.That(messages, Is.Empty);
    }

    [Test]
    public async Task GetStreamingResponseAsync_HandlesMultipleUpdates()
    {
        var storage = new InMemoryStorage();
        var memori = new Memori(storage);
        memori.Attribution("entity-1");
        memori.SetSession("test-session");

        var inner = new StreamingRecordingChatClient(
            [
                new ChatResponseUpdate(ChatRole.Assistant, "Hello "),
                new ChatResponseUpdate(ChatRole.Assistant, "world"),
                new ChatResponseUpdate(ChatRole.Assistant, "!"),
            ]);
        var client = new MemoriChatClient(inner, memori);

        var updates = new List<ChatResponseUpdate>();
        await foreach (var update in client.GetStreamingResponseAsync(
            [new ChatMessage(ChatRole.User, "Say hello")]))
        {
            updates.Add(update);
        }

        // Verify all updates were yielded
        Assert.That(updates, Has.Count.EqualTo(3));

        // Verify final message was captured
        var conversation = await storage.GetOrCreateConversationAsync("test-session", TimeSpan.FromMinutes(30));
        var messages = await storage.GetConversationMessagesAsync(conversation.Id);
        Assert.That(messages.Any(m => m.Role == ConversationRoles.Assistant && m.Content.Contains("Hello")), Is.True);
    }

    [Test]
    public async Task GetStreamingResponseAsync_HandlesMultipleAssistantMessages()
    {
        var storage = new InMemoryStorage();
        var memori = new Memori(storage);
        memori.Attribution("entity-1");
        memori.SetSession("test-session");

        var inner = new StreamingRecordingChatClient(
            [
                new ChatResponseUpdate(ChatRole.Assistant, "First response"),
                new ChatResponseUpdate(ChatRole.Assistant, "Second response"),
            ]);
        var client = new MemoriChatClient(inner, memori);

        var updates = new List<ChatResponseUpdate>();
        await foreach (var update in client.GetStreamingResponseAsync(
            [new ChatMessage(ChatRole.User, "Tell me two things")]))
        {
            updates.Add(update);
        }

        // Verify updates were yielded
        Assert.That(updates, Has.Count.EqualTo(2));

        // Verify messages were captured
        var conversation = await storage.GetOrCreateConversationAsync("test-session", TimeSpan.FromMinutes(30));
        var messages = await storage.GetConversationMessagesAsync(conversation.Id);
        Assert.That(messages.Count(m => m.Role == ConversationRoles.Assistant), Is.GreaterThanOrEqualTo(1));
    }

    [Test]
    public async Task GetStreamingResponseAsync_CapturesUserAndAssistantMessages()
    {
        var storage = new InMemoryStorage();
        var memori = new Memori(storage);
        memori.Attribution("entity-1");
        memori.SetSession("test-session");

        var inner = new StreamingRecordingChatClient(
            [new ChatResponseUpdate(ChatRole.Assistant, "response")]);
        var client = new MemoriChatClient(inner, memori);

        var updates = new List<ChatResponseUpdate>();
        await foreach (var update in client.GetStreamingResponseAsync(
            [new ChatMessage(ChatRole.User, "question")]))
        {
            updates.Add(update);
        }

        var conversation = await storage.GetOrCreateConversationAsync("test-session", TimeSpan.FromMinutes(30));
        var messages = await storage.GetConversationMessagesAsync(conversation.Id);

        // Verify both user and assistant messages were captured
        Assert.That(messages.Any(m => m.Role == ConversationRoles.User && m.Content == "question"), Is.True);
        Assert.That(messages.Any(m => m.Role == ConversationRoles.Assistant && m.Content.Contains("response")), Is.True);
    }

    [Test]
    public async Task GetStreamingResponseAsync_DoesNotCaptureWhenCancelled()
    {
        var storage = new InMemoryStorage();
        var memori = new Memori(storage);
        memori.Attribution("entity-1");
        memori.SetSession("test-session");

        var cts = new CancellationTokenSource();
        var inner = new CancellableStreamingChatClient(
            [
                new ChatResponseUpdate(ChatRole.Assistant, "part1"),
                new ChatResponseUpdate(ChatRole.Assistant, "part2"),
            ],
            cts);
        var client = new MemoriChatClient(inner, memori);

        var updates = new List<ChatResponseUpdate>();
        try
        {
            await foreach (var update in client.GetStreamingResponseAsync(
                [new ChatMessage(ChatRole.User, "test")],
                cancellationToken: cts.Token))
            {
                updates.Add(update);
                if (updates.Count == 1)
                    cts.Cancel();
            }
        }
        catch (OperationCanceledException)
        {
            // Expected
        }

        // Verify partial updates were yielded
        Assert.That(updates, Is.Not.Empty);

        // Verify nothing was captured due to cancellation
        var conversation = await storage.GetOrCreateConversationAsync("test-session", TimeSpan.FromMinutes(30));
        var messages = await storage.GetConversationMessagesAsync(conversation.Id);
        Assert.That(messages, Is.Empty);
    }

    [Test]
    public async Task GetStreamingResponseAsync_PreservesUpdateOrder()
    {
        var storage = new InMemoryStorage();
        var memori = new Memori(storage);
        memori.Attribution("entity-1");
        memori.SetSession("test-session");

        var inner = new StreamingRecordingChatClient(
            [
                new ChatResponseUpdate(ChatRole.Assistant, "1"),
                new ChatResponseUpdate(ChatRole.Assistant, "2"),
                new ChatResponseUpdate(ChatRole.Assistant, "3"),
            ]);
        var client = new MemoriChatClient(inner, memori);

        var updates = new List<ChatResponseUpdate>();
        await foreach (var update in client.GetStreamingResponseAsync(
            [new ChatMessage(ChatRole.User, "count to three")]))
        {
            updates.Add(update);
        }

        // Verify order is preserved
        Assert.That(updates[0].Text, Is.EqualTo("1"));
        Assert.That(updates[1].Text, Is.EqualTo("2"));
        Assert.That(updates[2].Text, Is.EqualTo("3"));
    }
}
