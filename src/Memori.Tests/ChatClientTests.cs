using Memori.Abstractions;
using Memori.Models;
using Memori.Storage;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.VectorData;
using NUnit.Framework;

namespace Memori.Tests;

public class ChatClientTests
{
    static readonly ChatRole DeveloperRole = new("developer");

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

    static async Task SeedFactAsync(VectorStoreCollection<string, MemoryFactRecord> factCollection, string entityId, string fact)
    {
        await factCollection.UpsertAsync(new MemoryFactRecord
        {
            Id = Guid.NewGuid().ToString("N"),
            EntityId = entityId,
            Content = fact,
            CreatedAt = DateTimeOffset.UtcNow,
        });
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

    static (InMemoryConversationStorage, VectorStoreCollection<string, MemoryFactRecord>) CreateStorage()
    {
        var conversationStorage = new InMemoryConversationStorage();
        var vectorStore = new InMemoryVectorStore();
        var factCollection = vectorStore.GetCollection<string, MemoryFactRecord>("memori_facts");
        return (conversationStorage, factCollection);
    }

    [Test]
    public async Task GetResponseAsync_UsesSharedPromptFormatter()
    {
        var (conversationStorage, factCollection) = CreateStorage();
        var options = new MemoriOptions
        {
            PromptContextTagName = "custom_context",
            PromptContextInstruction = "Follow the custom instruction.",
            PromptFactsHeading = "Custom facts:",
            IncludeFactTimestampsInPrompt = true,
            RecallRelevanceThreshold = 0,
        };
        var memori = new Memori(conversationStorage, factCollection, options);
        memori.Attribution("entity-1");
        memori.SetSession("test-session");

        await factCollection.UpsertAsync(new MemoryFactRecord
        {
            Id = Guid.NewGuid().ToString("N"),
            EntityId = "entity-1",
            Content = "The user lives in Karachi.",
            CreatedAt = new DateTimeOffset(2026, 5, 6, 12, 0, 0, TimeSpan.Zero),
            Summaries = [new MemorySummary("Lives in Karachi.", new DateTimeOffset(2026, 5, 6, 12, 30, 0, TimeSpan.Zero))]
        });

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
    public async Task GetResponseAsync_DefaultPromptInjection_InsertsBeforeAllMessages()
    {
        var (conversationStorage, factCollection) = CreateStorage();
        var memori = new Memori(conversationStorage, factCollection, new MemoriOptions { RecallRelevanceThreshold = 0 });
        memori.Attribution("entity-1");
        memori.SetSession("test-session");
        await SeedFactAsync(factCollection, "entity-1", "The user lives in Karachi.");

        var inner = new RecordingChatClient(new ChatResponse(new ChatMessage(ChatRole.Assistant, "answer")));
        var client = new MemoriChatClient(inner, memori);

        await client.GetResponseAsync(
            [
                new ChatMessage(ChatRole.System, "Host system instruction."),
                new ChatMessage(ChatRole.User, "Tell me about Karachi."),
            ]);

        Assert.That(inner.LastMessages[0].Role, Is.EqualTo(ChatRole.System));
        Assert.That(inner.LastMessages[0].Text, Does.Contain("The user lives in Karachi."));
        Assert.That(inner.LastMessages[1].Text, Is.EqualTo("Host system instruction."));
    }

    [Test]
    public async Task GetResponseAsync_PromptInjectionCanRunAfterSystemAndDeveloperMessages()
    {
        var (conversationStorage, factCollection) = CreateStorage();
        var options = new MemoriOptions
        {
            RecallRelevanceThreshold = 0,
            PromptInjectionPlacement = PromptInjectionPlacement.AfterSystemAndDeveloperMessages,
        };
        var memori = new Memori(conversationStorage, factCollection, options);
        memori.Attribution("entity-1");
        memori.SetSession("test-session");
        await SeedFactAsync(factCollection, "entity-1", "The user lives in Karachi.");

        var inner = new RecordingChatClient(new ChatResponse(new ChatMessage(ChatRole.Assistant, "answer")));
        var client = new MemoriChatClient(inner, memori);

        await client.GetResponseAsync(
            [
                new ChatMessage(ChatRole.System, "Host system instruction."),
                new ChatMessage(DeveloperRole, "Host developer instruction."),
                new ChatMessage(ChatRole.User, "Tell me about Karachi."),
            ]);

        Assert.That(inner.LastMessages[0].Text, Is.EqualTo("Host system instruction."));
        Assert.That(inner.LastMessages[1].Text, Is.EqualTo("Host developer instruction."));
        Assert.That(inner.LastMessages[2].Role, Is.EqualTo(ChatRole.System));
        Assert.That(inner.LastMessages[2].Text, Does.Contain("The user lives in Karachi."));
        Assert.That(inner.LastMessages[3].Role, Is.EqualTo(ChatRole.User));
    }

    [Test]
    public async Task GetResponseAsync_PromptInjectionCanUseDeveloperRole()
    {
        var (conversationStorage, factCollection) = CreateStorage();
        var options = new MemoriOptions
        {
            RecallRelevanceThreshold = 0,
            PromptInjectionRole = "developer",
            PromptInjectionPlacement = PromptInjectionPlacement.AfterSystemMessages,
        };
        var memori = new Memori(conversationStorage, factCollection, options);
        memori.Attribution("entity-1");
        memori.SetSession("test-session");
        await SeedFactAsync(factCollection, "entity-1", "The user lives in Karachi.");

        var inner = new RecordingChatClient(new ChatResponse(new ChatMessage(ChatRole.Assistant, "answer")));
        var client = new MemoriChatClient(inner, memori);

        await client.GetResponseAsync(
            [
                new ChatMessage(ChatRole.System, "Host system instruction."),
                new ChatMessage(ChatRole.User, "Tell me about Karachi."),
            ]);

        Assert.That(inner.LastMessages[0].Role, Is.EqualTo(ChatRole.System));
        Assert.That(inner.LastMessages[1].Role, Is.EqualTo(DeveloperRole));
        Assert.That(inner.LastMessages[1].Text, Does.Contain("The user lives in Karachi."));
    }

    [Test]
    public async Task GetResponseAsync_PromptInjectionCanBeDisabledSeparatelyFromRecall()
    {
        var (conversationStorage, factCollection) = CreateStorage();
        var options = new MemoriOptions
        {
            RecallRelevanceThreshold = 0,
            EnablePromptInjection = false,
        };
        var memori = new Memori(conversationStorage, factCollection, options);
        memori.Attribution("entity-1");
        memori.SetSession("test-session");
        await SeedFactAsync(factCollection, "entity-1", "The user lives in Karachi.");

        var inner = new RecordingChatClient(new ChatResponse(new ChatMessage(ChatRole.Assistant, "answer")));
        var client = new MemoriChatClient(inner, memori);

        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "Tell me about Karachi.")]);

        Assert.That(inner.LastMessages, Has.Count.EqualTo(1));
        Assert.That(inner.LastMessages[0].Role, Is.EqualTo(ChatRole.User));
    }

    [Test]
    public async Task GetResponseAsync_PromptInjectionCanMergeWithExistingInstruction()
    {
        var (conversationStorage, factCollection) = CreateStorage();
        var options = new MemoriOptions
        {
            RecallRelevanceThreshold = 0,
            PromptInjectionMergeStrategy = PromptInjectionMergeStrategy.AppendToLastMatchingRole,
        };
        var memori = new Memori(conversationStorage, factCollection, options);
        memori.Attribution("entity-1");
        memori.SetSession("test-session");
        await SeedFactAsync(factCollection, "entity-1", "The user lives in Karachi.");

        var inner = new RecordingChatClient(new ChatResponse(new ChatMessage(ChatRole.Assistant, "answer")));
        var client = new MemoriChatClient(inner, memori);

        await client.GetResponseAsync(
            [
                new ChatMessage(ChatRole.System, "Host system instruction."),
                new ChatMessage(ChatRole.User, "Tell me about Karachi."),
            ]);

        Assert.That(inner.LastMessages, Has.Count.EqualTo(2));
        Assert.That(inner.LastMessages[0].Role, Is.EqualTo(ChatRole.System));
        Assert.That(inner.LastMessages[0].Text, Does.StartWith("Host system instruction."));
        Assert.That(inner.LastMessages[0].Text, Does.Contain("The user lives in Karachi."));
        Assert.That(inner.LastMessages[1].Role, Is.EqualTo(ChatRole.User));
    }

    [Test]
    public async Task GetResponseAsync_DoesNotCaptureInjectedMemoryContext()
    {
        var (conversationStorage, factCollection) = CreateStorage();
        var options = new MemoriOptions
        {
            StripSystemMessagesOnCapture = false,
            RecallRelevanceThreshold = 0,
        };
        var memori = new Memori(conversationStorage, factCollection, options);
        memori.Attribution("entity-1");
        memori.SetSession("test-session");

        await factCollection.UpsertAsync(new MemoryFactRecord
        {
            Id = Guid.NewGuid().ToString("N"),
            EntityId = "entity-1",
            Content = "The user lives in Karachi.",
            CreatedAt = DateTimeOffset.UtcNow,
        });

        var inner = new RecordingChatClient(new ChatResponse(new ChatMessage(ChatRole.Assistant, "answer")));
        var client = new MemoriChatClient(inner, memori);

        await client.GetResponseAsync(
            [
                new ChatMessage(ChatRole.System, "Host system instruction."),
                new ChatMessage(ChatRole.User, "Where do I live?"),
            ]);

        await memori.WaitForAugmentationAsync();

        var conversation = await conversationStorage.GetOrCreateConversationAsync("test-session", TimeSpan.FromMinutes(30));
        var messages = await conversationStorage.GetConversationMessagesAsync(conversation.Id);

        Assert.That(messages.Count, Is.EqualTo(3));
        Assert.That(messages[0].Role, Is.EqualTo(ConversationRoles.System));
        Assert.That(messages[0].Content, Is.EqualTo("Host system instruction."));
        Assert.That(messages.Any(message => message.Content.Contains("memori_context", StringComparison.OrdinalIgnoreCase)), Is.False);
        Assert.That(messages.Any(message => message.Content.Contains("The user lives in Karachi.", StringComparison.OrdinalIgnoreCase)), Is.False);
        Assert.That(messages.Any(message => message.Role == ConversationRoles.Assistant && message.Content == "answer"), Is.True);
    }

    [Test]
    public async Task GetResponseAsync_CapturePolicyDropsConfiguredRoles()
    {
        var (conversationStorage, factCollection) = CreateStorage();
        var options = new MemoriOptions
        {
            StripSystemMessagesOnCapture = false,
        };
        options.ExcludedCaptureRoles.Add(ConversationRoles.System);
        options.ExcludedCaptureRoles.Add(ConversationRoles.Tool);

        var memori = new Memori(conversationStorage, factCollection, options);
        memori.Attribution("entity-1");
        memori.SetSession("test-session");

        var inner = new RecordingChatClient(new ChatResponse(new ChatMessage(ChatRole.Assistant, "answer")));
        var client = new MemoriChatClient(inner, memori);

        await client.GetResponseAsync(
            [
                new ChatMessage(ChatRole.System, "Host system instruction."),
                new ChatMessage(ChatRole.Tool, "Tool result."),
                new ChatMessage(ChatRole.User, "Hello"),
            ]);

        var conversation = await conversationStorage.GetOrCreateConversationAsync("test-session", TimeSpan.FromMinutes(30));
        var messages = await conversationStorage.GetConversationMessagesAsync(conversation.Id);

        Assert.That(messages.Select(message => message.Role), Is.EqualTo([
            ConversationRoles.User,
            ConversationRoles.Assistant,
        ]));
    }

    [Test]
    public async Task GetResponseAsync_CapturePolicyDropsMessagesUsingCustomPredicate()
    {
        var (conversationStorage, factCollection) = CreateStorage();
        var options = new MemoriOptions
        {
            CaptureMessageFilter = message =>
                !message.Content.Contains("do not store", StringComparison.OrdinalIgnoreCase),
        };
        var memori = new Memori(conversationStorage, factCollection, options);
        memori.Attribution("entity-1");
        memori.SetSession("test-session");

        var inner = new RecordingChatClient(new ChatResponse(new ChatMessage(ChatRole.Assistant, "answer")));
        var client = new MemoriChatClient(inner, memori);

        await client.GetResponseAsync(
            [
                new ChatMessage(ChatRole.User, "do not store this"),
                new ChatMessage(ChatRole.User, "store this"),
            ]);

        var conversation = await conversationStorage.GetOrCreateConversationAsync("test-session", TimeSpan.FromMinutes(30));
        var messages = await conversationStorage.GetConversationMessagesAsync(conversation.Id);

        Assert.That(messages.Any(message => message.Content == "do not store this"), Is.False);
        Assert.That(messages.Any(message => message.Content == "store this"), Is.True);
        Assert.That(messages.Any(message => message.Content == "answer"), Is.True);
    }

    [Test]
    public async Task GetResponseAsync_CapturePolicyRedactsMessagesUsingTransform()
    {
        var (conversationStorage, factCollection) = CreateStorage();
        var options = new MemoriOptions
        {
            CaptureMessageTransform = message => new ConversationMessage(
                message.Role,
                message.Content.Replace("secret-token", "[redacted]", StringComparison.OrdinalIgnoreCase),
                message.Type,
                message.CreatedAt,
                message.Metadata),
        };
        var memori = new Memori(conversationStorage, factCollection, options);
        memori.Attribution("entity-1");
        memori.SetSession("test-session");

        var inner = new RecordingChatClient(new ChatResponse(new ChatMessage(ChatRole.Assistant, "answer")));
        var client = new MemoriChatClient(inner, memori);

        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "my secret-token is abc")]);

        var conversation = await conversationStorage.GetOrCreateConversationAsync("test-session", TimeSpan.FromMinutes(30));
        var messages = await conversationStorage.GetConversationMessagesAsync(conversation.Id);

        Assert.That(messages.Any(message => message.Content.Contains("secret-token", StringComparison.OrdinalIgnoreCase)), Is.False);
        Assert.That(messages.Any(message => message.Content.Contains("[redacted]", StringComparison.OrdinalIgnoreCase)), Is.True);
    }

    [Test]
    public async Task GetResponseAsync_CapturePolicyDropsEmptyMessages()
    {
        var (conversationStorage, factCollection) = CreateStorage();
        var memori = new Memori(conversationStorage, factCollection, new MemoriOptions { DropEmptyMessagesOnCapture = true });
        memori.Attribution("entity-1");
        memori.SetSession("test-session");

        var inner = new RecordingChatClient(new ChatResponse(new ChatMessage(ChatRole.Assistant, "answer")));
        var client = new MemoriChatClient(inner, memori);

        await client.GetResponseAsync(
            [
                new ChatMessage(ChatRole.User, "   "),
                new ChatMessage(ChatRole.User, "store this"),
            ]);

        var conversation = await conversationStorage.GetOrCreateConversationAsync("test-session", TimeSpan.FromMinutes(30));
        var messages = await conversationStorage.GetConversationMessagesAsync(conversation.Id);

        Assert.That(messages.Any(message => string.IsNullOrWhiteSpace(message.Content)), Is.False);
        Assert.That(messages.Any(message => message.Content == "store this"), Is.True);
    }

    [Test]
    public async Task GetResponseAsync_InjectsMemoryContextAndCapturesResponse()
    {
        var (conversationStorage, factCollection) = CreateStorage();
        var memori = new Memori(conversationStorage, factCollection, augmentationClient: new TestAugmentationClient());
        memori.Attribution("entity-1");
        memori.SetSession("test-session");

        var inner = new RecordingChatClient(new ChatResponse(new ChatMessage(ChatRole.Assistant, "answer")));
        var client = new MemoriChatClient(inner, memori);

        var response = await client.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "What do you remember about me?")]);

        Assert.That(response.Messages.Single().Text, Is.EqualTo("answer"));

        await memori.WaitForAugmentationAsync();

        // Search for facts in vector store
        var factResults = new List<MemoryFactRecord>();
        await foreach (var result in factCollection.SearchAsync("answer", 10, new VectorSearchOptions<MemoryFactRecord> { Filter = r => r.EntityId == "entity-1" }))
        {
            factResults.Add(result.Record);
        }
        Assert.That(factResults, Is.Not.Empty);
    }

    [Test]
    public async Task GetStreamingResponseAsync_CapturesFinalAssistantMessage()
    {
        var (conversationStorage, factCollection) = CreateStorage();
        var memori = new Memori(conversationStorage, factCollection);
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
        var conversation = await conversationStorage.GetOrCreateConversationAsync("test-session", TimeSpan.FromMinutes(30));
        var messages = await conversationStorage.GetConversationMessagesAsync(conversation.Id);
        Assert.That(messages.Any(message => message.Role == ConversationRoles.Assistant), Is.True);
    }

    [Test]
    public async Task GetResponseAsync_CapturesProviderResponseMetadata()
    {
        var (conversationStorage, factCollection) = CreateStorage();
        var memori = new Memori(conversationStorage, factCollection);
        memori.Attribution("entity-1");
        memori.SetSession("test-session");

        var response = new ChatResponse(new ChatMessage(ChatRole.Assistant, "answer"))
        {
            ResponseId = "response-1",
            ConversationId = "provider-conversation-1",
            ModelId = "model-1",
            CreatedAt = new DateTimeOffset(2026, 5, 7, 10, 0, 0, TimeSpan.Zero),
            AdditionalProperties = new AdditionalPropertiesDictionary
            {
                ["provider_trace_id"] = "trace-1",
            },
        };
        var inner = new RecordingChatClient(response);
        var client = new MemoriChatClient(inner, memori);

        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "Hello")]);

        var conversation = await conversationStorage.GetOrCreateConversationAsync("test-session", TimeSpan.FromMinutes(30));
        var messages = await conversationStorage.GetConversationMessagesAsync(conversation.Id);
        var assistant = messages.Single(message => message.Role == ConversationRoles.Assistant);

        Assert.That(assistant.Metadata["memori.provider.response_id"], Is.EqualTo("response-1"));
        Assert.That(assistant.Metadata["memori.provider.conversation_id"], Is.EqualTo("provider-conversation-1"));
        Assert.That(assistant.Metadata["memori.provider.model_id"], Is.EqualTo("model-1"));
        var additional = (IReadOnlyDictionary<string, object?>)assistant.Metadata["memori.provider.response_additional_properties"]!;
        Assert.That(additional["provider_trace_id"], Is.EqualTo("trace-1"));
    }

    [Test]
    public async Task GetStreamingResponseAsync_CapturesProviderUpdateMetadataAndContinuationTokens()
    {
        var (conversationStorage, factCollection) = CreateStorage();
        var memori = new Memori(conversationStorage, factCollection);
        memori.Attribution("entity-1");
        memori.SetSession("test-session");

        var first = new ChatResponseUpdate(ChatRole.Assistant, "streamed ")
        {
            ResponseId = "response-1",
            MessageId = "message-1",
            ConversationId = "provider-conversation-1",
            ModelId = "model-1",
        };
        var second = new ChatResponseUpdate(ChatRole.Assistant, "answer")
        {
            ResponseId = "response-1",
            MessageId = "message-1",
            ConversationId = "provider-conversation-1",
            ModelId = "model-1",
        };
        var inner = new StreamingRecordingChatClient([first, second]);
        var client = new MemoriChatClient(inner, memori);

        await foreach (var _ in client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "Hello")]))
        { }

        var conversation = await conversationStorage.GetOrCreateConversationAsync("test-session", TimeSpan.FromMinutes(30));
        var messages = await conversationStorage.GetConversationMessagesAsync(conversation.Id);
        var assistant = messages.Single(message => message.Role == ConversationRoles.Assistant);

        Assert.That(assistant.Metadata["memori.provider.response_id"], Is.EqualTo("response-1"));
        Assert.That(assistant.Metadata["memori.provider.conversation_id"], Is.EqualTo("provider-conversation-1"));
        Assert.That(assistant.Metadata["memori.provider.model_id"], Is.EqualTo("model-1"));
        Assert.That((string[])assistant.Metadata["memori.provider.streaming_message_ids"]!, Is.EqualTo(["message-1"]));
    }

    [Test]
    public async Task GetResponseAsync_SkipsRecallWhenDisabled()
    {
        var (conversationStorage, factCollection) = CreateStorage();
        var memori = new Memori(conversationStorage, factCollection);
        memori.Attribution("entity-1");
        memori.SetSession("test-session");

        await factCollection.UpsertAsync(new MemoryFactRecord
        {
            Id = Guid.NewGuid().ToString("N"),
            EntityId = "entity-1",
            Content = "The user lives in Karachi.",
            CreatedAt = DateTimeOffset.UtcNow,
        });

        var inner = new RecordingChatClient(new ChatResponse(new ChatMessage(ChatRole.Assistant, "answer")));
        var client = new MemoriChatClient(inner, memori);

        var requestOptions = new MemoriRequestOptions { EnableRecall = false };
        var options = new ChatOptions();
        options.AdditionalProperties ??= new AdditionalPropertiesDictionary();
        options.AdditionalProperties[MemoriChatClient.MemoriRequestOptionsKey] = requestOptions;

        await client.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "Tell me about Karachi.")],
            options);

        var systemMessages = inner.LastMessages.Where(m => m.Role == ChatRole.System).ToList();
        Assert.That(systemMessages, Is.Empty);
    }

    [Test]
    public async Task GetResponseAsync_SkipsCaptureWhenDisabled()
    {
        var (conversationStorage, factCollection) = CreateStorage();
        var memori = new Memori(conversationStorage, factCollection);
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

        var conversation = await conversationStorage.GetOrCreateConversationAsync("test-session", TimeSpan.FromMinutes(30));
        var messages = await conversationStorage.GetConversationMessagesAsync(conversation.Id);

        Assert.That(messages, Is.Empty);
    }

    [Test]
    public async Task GetResponseAsync_CaptureOnlyMode()
    {
        var (conversationStorage, factCollection) = CreateStorage();
        var memori = new Memori(conversationStorage, factCollection);
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

        var systemMessages = inner.LastMessages.Where(m => m.Role == ChatRole.System).ToList();
        Assert.That(systemMessages, Is.Empty);

        var conversation = await conversationStorage.GetOrCreateConversationAsync("test-session", TimeSpan.FromMinutes(30));
        var messages = await conversationStorage.GetConversationMessagesAsync(conversation.Id);
        Assert.That(messages, Is.Not.Empty);
        Assert.That(messages.Any(m => m.Content == "Hello"), Is.True);
        Assert.That(messages.Any(m => m.Content == "answer"), Is.True);
    }

    [Test]
    public async Task GetStreamingResponseAsync_SkipsRecallWhenDisabled()
    {
        var (conversationStorage, factCollection) = CreateStorage();
        var memori = new Memori(conversationStorage, factCollection);
        memori.Attribution("entity-1");
        memori.SetSession("test-session");

        await factCollection.UpsertAsync(new MemoryFactRecord
        {
            Id = Guid.NewGuid().ToString("N"),
            EntityId = "entity-1",
            Content = "The user lives in Karachi.",
            CreatedAt = DateTimeOffset.UtcNow,
        });

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

        var systemMessages = inner.LastMessages.Where(m => m.Role == ChatRole.System).ToList();
        Assert.That(systemMessages, Is.Empty);
    }

    [Test]
    public async Task GetStreamingResponseAsync_SkipsCaptureWhenDisabled()
    {
        var (conversationStorage, factCollection) = CreateStorage();
        var memori = new Memori(conversationStorage, factCollection);
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

        var conversation = await conversationStorage.GetOrCreateConversationAsync("test-session", TimeSpan.FromMinutes(30));
        var messages = await conversationStorage.GetConversationMessagesAsync(conversation.Id);

        Assert.That(messages, Is.Empty);
    }

    [Test]
    public async Task GetStreamingResponseAsync_HandlesMultipleUpdates()
    {
        var (conversationStorage, factCollection) = CreateStorage();
        var memori = new Memori(conversationStorage, factCollection);
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

        Assert.That(updates, Has.Count.EqualTo(3));

        var conversation = await conversationStorage.GetOrCreateConversationAsync("test-session", TimeSpan.FromMinutes(30));
        var messages = await conversationStorage.GetConversationMessagesAsync(conversation.Id);
        Assert.That(messages.Any(m => m.Role == ConversationRoles.Assistant && m.Content.Contains("Hello")), Is.True);
    }

    [Test]
    public async Task GetStreamingResponseAsync_HandlesMultipleAssistantMessages()
    {
        var (conversationStorage, factCollection) = CreateStorage();
        var memori = new Memori(conversationStorage, factCollection);
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

        Assert.That(updates, Has.Count.EqualTo(2));

        var conversation = await conversationStorage.GetOrCreateConversationAsync("test-session", TimeSpan.FromMinutes(30));
        var messages = await conversationStorage.GetConversationMessagesAsync(conversation.Id);
        Assert.That(messages.Count(m => m.Role == ConversationRoles.Assistant), Is.GreaterThanOrEqualTo(1));
    }

    [Test]
    public async Task GetStreamingResponseAsync_CapturesUserAndAssistantMessages()
    {
        var (conversationStorage, factCollection) = CreateStorage();
        var memori = new Memori(conversationStorage, factCollection);
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

        var conversation = await conversationStorage.GetOrCreateConversationAsync("test-session", TimeSpan.FromMinutes(30));
        var messages = await conversationStorage.GetConversationMessagesAsync(conversation.Id);

        Assert.That(messages.Any(m => m.Role == ConversationRoles.User && m.Content == "question"), Is.True);
        Assert.That(messages.Any(m => m.Role == ConversationRoles.Assistant && m.Content.Contains("response")), Is.True);
    }

    [Test]
    public async Task GetStreamingResponseAsync_DoesNotCaptureWhenCancelled()
    {
        var (conversationStorage, factCollection) = CreateStorage();
        var memori = new Memori(conversationStorage, factCollection);
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
        }

        Assert.That(updates, Is.Not.Empty);

        var conversation = await conversationStorage.GetOrCreateConversationAsync("test-session", TimeSpan.FromMinutes(30));
        var messages = await conversationStorage.GetConversationMessagesAsync(conversation.Id);
        Assert.That(messages, Is.Empty);
    }

    [Test]
    public async Task GetStreamingResponseAsync_PreservesUpdateOrder()
    {
        var (conversationStorage, factCollection) = CreateStorage();
        var memori = new Memori(conversationStorage, factCollection);
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

        Assert.That(updates[0].Text, Is.EqualTo("1"));
        Assert.That(updates[1].Text, Is.EqualTo("2"));
        Assert.That(updates[2].Text, Is.EqualTo("3"));
    }
}
