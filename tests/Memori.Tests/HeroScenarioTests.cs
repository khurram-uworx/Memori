using Memori.Abstractions;
using Memori.Models;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.VectorData;
using NUnit.Framework;

namespace Memori.Tests;

public class HeroScenarioTests
{
    [Test]
    public async Task HeroScenario_CapturesAndRecallsAcrossTurns()
    {
        var memori = TestMemoriFactory.Create(augmentationClient: new HeroAugmentationClient());
        memori.Attribution("user_123", "support_agent");
        memori.SetSession("hero-session");

        var innerClient = new HeroChatClient();
        var chatClient = new MemoriChatClient(innerClient, memori);

        var firstResponse = await chatClient.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "My favorite color is blue and I live in Karachi.")]);

        Assert.That(firstResponse.Text, Is.EqualTo("Noted."));
        Assert.That(innerClient.Calls, Has.Count.EqualTo(1));
        Assert.That(innerClient.Calls[0].Any(message => message.Role == ChatRole.System), Is.False);

        await memori.WaitForAugmentationAsync();

        var secondResponse = await chatClient.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "What is my favorite color and where do I live?")]);

        Assert.That(secondResponse.Text, Is.EqualTo("You like blue and live in Karachi."));
        Assert.That(innerClient.Calls, Has.Count.EqualTo(2));
        Assert.That(innerClient.Calls[1].Any(message => message.Role == ChatRole.System &&
            message.Text is not null &&
            message.Text.Contains("favorite color is blue", StringComparison.OrdinalIgnoreCase)), Is.True);

        var factCollection = GetFactCollection(memori);
        var conversationStorage = GetConversationStorage(memori);
        var entityId = await conversationStorage.GetOrCreateEntityAsync("user_123");
        var factResults = new List<VectorSearchResult<MemoryFactRecord>>();
        await foreach (var result in factCollection.SearchAsync("favorite", 10, new VectorSearchOptions<MemoryFactRecord> { Filter = r => r.EntityId == entityId }))
        {
            factResults.Add(result);
        }

        Assert.That(factResults, Is.Not.Empty);
        Assert.That(factResults.Any(r => r.Record.Content.Contains("favorite color is blue", StringComparison.OrdinalIgnoreCase)), Is.True);

        var conversation = await conversationStorage.GetOrCreateConversationAsync("hero-session", TimeSpan.FromMinutes(30));
        var messages = await conversationStorage.GetConversationMessagesAsync(conversation.Id);
        Assert.That(messages.Count, Is.GreaterThanOrEqualTo(4));
    }

    static IConversationStorage GetConversationStorage(MemoriEngine memori)
    {
        var field = typeof(MemoriEngine).GetField("conversationStorage", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        return (IConversationStorage)field!.GetValue(memori)!;
    }

    static VectorStoreCollection<string, MemoryFactRecord> GetFactCollection(MemoriEngine memori)
    {
        var field = typeof(MemoriEngine).GetField("factCollection", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        return (VectorStoreCollection<string, MemoryFactRecord>)field!.GetValue(memori)!;
    }

    sealed class HeroChatClient : IChatClient
    {
        public List<IReadOnlyList<ChatMessage>> Calls { get; } = [];

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            var call = messages.ToArray();
            Calls.Add(call);

            var userMessage = call.Last(message => message.Role == ChatRole.User).Text ?? string.Empty;
            var responseText = userMessage.Contains("favorite color", StringComparison.OrdinalIgnoreCase) &&
                               userMessage.Contains("where do I live", StringComparison.OrdinalIgnoreCase)
                ? "You like blue and live in Karachi."
                : "Noted.";

            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, responseText)));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var response = await GetResponseAsync(messages, options, cancellationToken).ConfigureAwait(false);
            foreach (var update in response.ToChatResponseUpdates())
            {
                yield return update;
            }
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    sealed class HeroAugmentationClient : IAugmentationClient
    {
        public ValueTask<AugmentationResult?> AugmentAsync(
            AugmentationInput context,
            CancellationToken cancellationToken = default)
        {
            var facts = new List<NewMemoryFact>();

            foreach (var message in context.Messages)
            {
                if (message.Role == ConversationRoles.User &&
                    message.Content.Contains("favorite color is blue", StringComparison.OrdinalIgnoreCase))
                {
                    facts.Add(new NewMemoryFact("The user said their favorite color is blue."));
                    facts.Add(new NewMemoryFact("The user lives in Karachi."));
                }
            }

            return ValueTask.FromResult<AugmentationResult?>(
                new AugmentationResult(Facts: facts));
        }
    }
}
