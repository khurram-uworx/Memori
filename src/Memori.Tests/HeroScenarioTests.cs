using Memori.Abstractions;
using Memori.Models;
using Memori.Storage;
using Microsoft.Extensions.AI;
using NUnit.Framework;

namespace Memori.Tests;

public class HeroScenarioTests
{
    [Test]
    public async Task HeroScenario_CapturesAndRecallsAcrossTurns()
    {
        var storage = new InMemoryStorage();
        var memori = new Memori(
            storage,
            augmentationClient: new HeroAugmentationClient());
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

        var entityId = await storage.GetOrCreateEntityAsync("user_123");
        var facts = await storage.SearchFactsAsync(entityId, "favorite color", null, 10, 10);

        Assert.That(facts, Is.Not.Empty);
        Assert.That(facts.Any(fact => fact.Content.Contains("favorite color is blue", StringComparison.OrdinalIgnoreCase)), Is.True);
        Assert.That(facts.Any(fact => fact.Content.Contains("lives in Karachi", StringComparison.OrdinalIgnoreCase)), Is.False);

        var conversations = await storage.GetOrCreateConversationAsync("hero-session", TimeSpan.FromMinutes(30));
        var messages = await storage.GetConversationMessagesAsync(conversations.Id);
        Assert.That(messages.Count, Is.GreaterThanOrEqualTo(4));
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
