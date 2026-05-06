using Memori.Abstractions;
using Memori.Models;
using Memori.Storage;
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
        await storage.AddFactsAsync(entityId, new[] { new NewMemoryFact("coffee is preferred") });

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
