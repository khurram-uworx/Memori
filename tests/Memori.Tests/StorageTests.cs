using Memori.Abstractions;
using Memori.Models;
using Memori.Storage;
using NUnit.Framework;

namespace Memori.Tests;

/// <summary>
/// Reusable contract-style tests for <see cref="IConversationStorage"/> implementations.
/// </summary>
public abstract class ConversationStorageContractTests
{
    /// <summary>
    /// Creates a fresh conversation storage instance for each contract test.
    /// </summary>
    protected abstract IConversationStorage CreateConversationStorage();

    #region Idempotent Get-or-Create Behavior

    [Test]
    public async Task GetOrCreateEntityAsync_IsIdempotent()
    {
        var storage = CreateConversationStorage();

        var first = await storage.GetOrCreateEntityAsync("user-1");
        var second = await storage.GetOrCreateEntityAsync("user-1");

        Assert.That(first, Is.EqualTo(second));
    }

    [Test]
    public async Task GetOrCreateEntityAsync_ReturnsDifferentIdsForDifferentExternalIds()
    {
        var storage = CreateConversationStorage();

        var id1 = await storage.GetOrCreateEntityAsync("user-1");
        var id2 = await storage.GetOrCreateEntityAsync("user-2");

        Assert.That(id1, Is.Not.EqualTo(id2));
    }

    [Test]
    public async Task GetOrCreateProcessAsync_IsIdempotent()
    {
        var storage = CreateConversationStorage();

        var first = await storage.GetOrCreateProcessAsync("workflow-1");
        var second = await storage.GetOrCreateProcessAsync("workflow-1");

        Assert.That(first, Is.EqualTo(second));
    }

    [Test]
    public async Task GetOrCreateProcessAsync_ReturnsDifferentIdsForDifferentExternalIds()
    {
        var storage = CreateConversationStorage();

        var id1 = await storage.GetOrCreateProcessAsync("workflow-1");
        var id2 = await storage.GetOrCreateProcessAsync("workflow-2");

        Assert.That(id1, Is.Not.EqualTo(id2));
    }

    [Test]
    public async Task GetOrCreateSessionAsync_IsIdempotent()
    {
        var storage = CreateConversationStorage();

        var first = await storage.GetOrCreateSessionAsync("session-1", "entity-1", "process-1");
        var second = await storage.GetOrCreateSessionAsync("session-1", null, null);

        Assert.That(first, Is.EqualTo(second));
    }

    [Test]
    public async Task GetOrCreateSessionAsync_PreservesEntityAndProcessOnFirstCall()
    {
        var storage = CreateConversationStorage();
        var entityId = await storage.GetOrCreateEntityAsync("user-1");
        var processId = await storage.GetOrCreateProcessAsync("workflow-1");

        var sessionId = await storage.GetOrCreateSessionAsync("session-1", entityId, processId);

        Assert.That(sessionId, Is.Not.Null);
    }

    [Test]
    public async Task GetOrCreateSessionAsync_MergesEntityAndProcessOnSubsequentCalls()
    {
        var storage = CreateConversationStorage();
        var entityId = await storage.GetOrCreateEntityAsync("user-1");
        var processId = await storage.GetOrCreateProcessAsync("workflow-1");

        var sessionId = await storage.GetOrCreateSessionAsync("session-1", entityId, null);
        var sameSessionId = await storage.GetOrCreateSessionAsync("session-1", null, processId);

        Assert.That(sameSessionId, Is.EqualTo(sessionId));
    }

    #endregion

    #region Conversation Timeout Behavior

    [Test]
    public async Task GetOrCreateConversationAsync_CreatesNewConversationWhenNoneExists()
    {
        var storage = CreateConversationStorage();

        var conversation = await storage.GetOrCreateConversationAsync("session-1", TimeSpan.FromMinutes(5));

        Assert.That(conversation, Is.Not.Null);
        Assert.That(conversation.Id, Is.Not.Null);
        Assert.That(conversation.SessionId, Is.EqualTo("session-1"));
    }

    [Test]
    public async Task GetOrCreateConversationAsync_ReturnsExistingConversationWithinTimeout()
    {
        var storage = CreateConversationStorage();

        var first = await storage.GetOrCreateConversationAsync("session-1", TimeSpan.FromMinutes(5));
        var second = await storage.GetOrCreateConversationAsync("session-1", TimeSpan.FromMinutes(5));

        Assert.That(first.Id, Is.EqualTo(second.Id));
    }

    [Test]
    public async Task GetOrCreateConversationAsync_CreatesNewConversationAfterTimeout()
    {
        var storage = CreateConversationStorage();

        var first = await storage.GetOrCreateConversationAsync("session-1", TimeSpan.FromMilliseconds(100));
        await Task.Delay(150);
        var second = await storage.GetOrCreateConversationAsync("session-1", TimeSpan.FromMilliseconds(100));

        Assert.That(first.Id, Is.Not.EqualTo(second.Id));
    }

    [Test]
    public void GetOrCreateConversationAsync_ThrowsOnInvalidTimeout()
    {
        var storage = CreateConversationStorage();

        Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => storage.GetOrCreateConversationAsync("session-1", TimeSpan.Zero).AsTask());

        Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => storage.GetOrCreateConversationAsync("session-1", TimeSpan.FromMilliseconds(-1)).AsTask());
    }

    #endregion

    #region Message Ordering

    [Test]
    public async Task AppendMessagesAsync_PreservesMessageOrder()
    {
        var storage = CreateConversationStorage();
        var conversation = await storage.GetOrCreateConversationAsync("session-1", TimeSpan.FromMinutes(5));

        await storage.AppendMessagesAsync(conversation.Id, new[]
        {
            new ConversationMessage(ConversationRoles.User, "hello"),
            new ConversationMessage(ConversationRoles.Assistant, "world"),
            new ConversationMessage(ConversationRoles.User, "how are you"),
        });

        var messages = await storage.GetConversationMessagesAsync(conversation.Id);

        Assert.That(messages.Select(m => m.Content), Is.EqualTo(new[] { "hello", "world", "how are you" }));
    }

    [Test]
    public async Task AppendMessagesAsync_AllowsMultipleAppends()
    {
        var storage = CreateConversationStorage();
        var conversation = await storage.GetOrCreateConversationAsync("session-1", TimeSpan.FromMinutes(5));

        await storage.AppendMessagesAsync(conversation.Id, new[]
        {
            new ConversationMessage(ConversationRoles.User, "first"),
        });

        await storage.AppendMessagesAsync(conversation.Id, new[]
        {
            new ConversationMessage(ConversationRoles.Assistant, "second"),
        });

        var messages = await storage.GetConversationMessagesAsync(conversation.Id);

        Assert.That(messages.Select(m => m.Content), Is.EqualTo(new[] { "first", "second" }));
    }

    [Test]
    public async Task GetConversationMessagesAsync_ReturnsEmptyListForNewConversation()
    {
        var storage = CreateConversationStorage();
        var conversation = await storage.GetOrCreateConversationAsync("session-1", TimeSpan.FromMinutes(5));

        var messages = await storage.GetConversationMessagesAsync(conversation.Id);

        Assert.That(messages, Is.Empty);
    }

    [Test]
    public async Task AppendMessagesAsync_UpdatesConversationTimestamp()
    {
        var storage = CreateConversationStorage();
        var conversation = await storage.GetOrCreateConversationAsync("session-1", TimeSpan.FromMinutes(5));
        var originalUpdatedAt = conversation.UpdatedAt;

        await Task.Delay(10);
        await storage.AppendMessagesAsync(conversation.Id, new[]
        {
            new ConversationMessage(ConversationRoles.User, "hello"),
        });

        var updatedConversation = await storage.GetOrCreateConversationAsync("session-1", TimeSpan.FromMinutes(5));

        Assert.That(updatedConversation.UpdatedAt, Is.GreaterThan(originalUpdatedAt));
    }

    #endregion

    #region Conversation Summary

    [Test]
    public async Task UpdateConversationSummaryAsync_UpdatesSummary()
    {
        var storage = CreateConversationStorage();
        var conversation = await storage.GetOrCreateConversationAsync("session-1", TimeSpan.FromMinutes(5));

        await storage.UpdateConversationSummaryAsync(conversation.Id, "This is a summary");

        var updatedConversation = await storage.GetOrCreateConversationAsync("session-1", TimeSpan.FromMinutes(5));

        Assert.That(updatedConversation.Summary, Is.EqualTo("This is a summary"));
    }

    [Test]
    public async Task UpdateConversationSummaryAsync_UpdatesTimestamp()
    {
        var storage = CreateConversationStorage();
        var conversation = await storage.GetOrCreateConversationAsync("session-1", TimeSpan.FromMinutes(5));
        var originalUpdatedAt = conversation.UpdatedAt;

        await Task.Delay(10);
        await storage.UpdateConversationSummaryAsync(conversation.Id, "Summary");

        var updatedConversation = await storage.GetOrCreateConversationAsync("session-1", TimeSpan.FromMinutes(5));

        Assert.That(updatedConversation.UpdatedAt, Is.GreaterThan(originalUpdatedAt));
    }

    #endregion
}

/// <summary>
/// Contract tests for the reference <see cref="InMemoriConversationStorage"/> implementation.
/// </summary>
public sealed class InMemoryConversationStorageTests : ConversationStorageContractTests
{
    protected override IConversationStorage CreateConversationStorage() => new InMemoriConversationStorage();
}
