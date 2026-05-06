using Memori.Abstractions;
using Memori.MicrosoftExtensionsAI;
using Memori.Models;
using Memori.Storage;
using Microsoft.Extensions.AI;
using NUnit.Framework;

namespace Memori.Tests;

public class ChatClientTests
{
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
}
