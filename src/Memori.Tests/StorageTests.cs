using Memori.Models;
using Memori.Storage;
using NUnit.Framework;

namespace Memori.Tests;

public class StorageTests
{
    [Test]
    public async Task GetOrCreateSessionAsync_IsIdempotent()
    {
        var storage = new InMemoryStorage();

        var first = await storage.GetOrCreateSessionAsync("session-1", "entity-1", "process-1");
        var second = await storage.GetOrCreateSessionAsync("session-1", null, null);

        Assert.That(first, Is.EqualTo(second));
    }

    [Test]
    public async Task AppendMessagesAsync_PreservesMessageOrder()
    {
        var storage = new InMemoryStorage();
        var conversation = await storage.GetOrCreateConversationAsync("session-1", TimeSpan.FromMinutes(5));

        await storage.AppendMessagesAsync(conversation.Id, new[]
        {
            new ConversationMessage(ConversationRoles.User, "hello"),
            new ConversationMessage(ConversationRoles.Assistant, "world"),
        });

        var messages = await storage.GetConversationMessagesAsync(conversation.Id);

        Assert.That(messages.Select(message => message.Content), Is.EqualTo(new[] { "hello", "world" }));
    }

    [Test]
    public async Task SearchFactsAsync_ReturnsBestRankedFacts()
    {
        var storage = new InMemoryStorage();
        var entityId = await storage.GetOrCreateEntityAsync("entity-1");
        await storage.AddFactsAsync(entityId, new[]
        {
            new NewMemoryFact("the user likes hiking"),
            new NewMemoryFact("the user enjoys coffee"),
        });

        var results = await storage.SearchFactsAsync(entityId, "coffee", null, 5, 10);

        Assert.That(results, Is.Not.Empty);
        Assert.That(results.First().Content, Does.Contain("coffee"));
    }
}
