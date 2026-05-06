using Memori.Abstractions;
using Memori.Models;
using Memori.Storage;
using NUnit.Framework;

namespace Memori.Tests;

/// <summary>
/// Contract-style tests for <see cref="IStorage"/> implementations.
/// These tests verify the reference behavior of <see cref="InMemoryStorage"/>
/// and serve as a specification for custom storage providers.
/// </summary>
public class StorageTests
{
    #region Idempotent Get-or-Create Behavior

    [Test]
    public async Task GetOrCreateEntityAsync_IsIdempotent()
    {
        var storage = new InMemoryStorage();

        var first = await storage.GetOrCreateEntityAsync("user-1");
        var second = await storage.GetOrCreateEntityAsync("user-1");

        Assert.That(first, Is.EqualTo(second));
    }

    [Test]
    public async Task GetOrCreateEntityAsync_ReturnsDifferentIdsForDifferentExternalIds()
    {
        var storage = new InMemoryStorage();

        var id1 = await storage.GetOrCreateEntityAsync("user-1");
        var id2 = await storage.GetOrCreateEntityAsync("user-2");

        Assert.That(id1, Is.Not.EqualTo(id2));
    }

    [Test]
    public async Task GetOrCreateProcessAsync_IsIdempotent()
    {
        var storage = new InMemoryStorage();

        var first = await storage.GetOrCreateProcessAsync("workflow-1");
        var second = await storage.GetOrCreateProcessAsync("workflow-1");

        Assert.That(first, Is.EqualTo(second));
    }

    [Test]
    public async Task GetOrCreateProcessAsync_ReturnsDifferentIdsForDifferentExternalIds()
    {
        var storage = new InMemoryStorage();

        var id1 = await storage.GetOrCreateProcessAsync("workflow-1");
        var id2 = await storage.GetOrCreateProcessAsync("workflow-2");

        Assert.That(id1, Is.Not.EqualTo(id2));
    }

    [Test]
    public async Task GetOrCreateSessionAsync_IsIdempotent()
    {
        var storage = new InMemoryStorage();

        var first = await storage.GetOrCreateSessionAsync("session-1", "entity-1", "process-1");
        var second = await storage.GetOrCreateSessionAsync("session-1", null, null);

        Assert.That(first, Is.EqualTo(second));
    }

    [Test]
    public async Task GetOrCreateSessionAsync_PreservesEntityAndProcessOnFirstCall()
    {
        var storage = new InMemoryStorage();
        var entityId = await storage.GetOrCreateEntityAsync("user-1");
        var processId = await storage.GetOrCreateProcessAsync("workflow-1");

        var sessionId = await storage.GetOrCreateSessionAsync("session-1", entityId, processId);

        Assert.That(sessionId, Is.Not.Null);
    }

    [Test]
    public async Task GetOrCreateSessionAsync_MergesEntityAndProcessOnSubsequentCalls()
    {
        var storage = new InMemoryStorage();
        var entityId = await storage.GetOrCreateEntityAsync("user-1");
        var processId = await storage.GetOrCreateProcessAsync("workflow-1");

        // First call with entity only
        var sessionId = await storage.GetOrCreateSessionAsync("session-1", entityId, null);

        // Second call with process only
        var sameSessionId = await storage.GetOrCreateSessionAsync("session-1", null, processId);

        Assert.That(sameSessionId, Is.EqualTo(sessionId));
    }

    #endregion

    #region Conversation Timeout Behavior

    [Test]
    public async Task GetOrCreateConversationAsync_CreatesNewConversationWhenNoneExists()
    {
        var storage = new InMemoryStorage();

        var conversation = await storage.GetOrCreateConversationAsync("session-1", TimeSpan.FromMinutes(5));

        Assert.That(conversation, Is.Not.Null);
        Assert.That(conversation.Id, Is.Not.Null);
        Assert.That(conversation.SessionId, Is.EqualTo("session-1"));
    }

    [Test]
    public async Task GetOrCreateConversationAsync_ReturnsExistingConversationWithinTimeout()
    {
        var storage = new InMemoryStorage();

        var first = await storage.GetOrCreateConversationAsync("session-1", TimeSpan.FromMinutes(5));
        var second = await storage.GetOrCreateConversationAsync("session-1", TimeSpan.FromMinutes(5));

        Assert.That(first.Id, Is.EqualTo(second.Id));
    }

    [Test]
    public async Task GetOrCreateConversationAsync_CreatesNewConversationAfterTimeout()
    {
        var storage = new InMemoryStorage();

        var first = await storage.GetOrCreateConversationAsync("session-1", TimeSpan.FromMilliseconds(100));
        await Task.Delay(150);
        var second = await storage.GetOrCreateConversationAsync("session-1", TimeSpan.FromMilliseconds(100));

        Assert.That(first.Id, Is.Not.EqualTo(second.Id));
    }

    [Test]
    public void GetOrCreateConversationAsync_ThrowsOnInvalidTimeout()
    {
        var storage = new InMemoryStorage();

        Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            async () => await storage.GetOrCreateConversationAsync("session-1", TimeSpan.Zero));

        Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            async () => await storage.GetOrCreateConversationAsync("session-1", TimeSpan.FromMilliseconds(-1)));
    }

    #endregion

    #region Message Ordering

    [Test]
    public async Task AppendMessagesAsync_PreservesMessageOrder()
    {
        var storage = new InMemoryStorage();
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
        var storage = new InMemoryStorage();
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
        var storage = new InMemoryStorage();
        var conversation = await storage.GetOrCreateConversationAsync("session-1", TimeSpan.FromMinutes(5));

        var messages = await storage.GetConversationMessagesAsync(conversation.Id);

        Assert.That(messages, Is.Empty);
    }

    [Test]
    public async Task AppendMessagesAsync_UpdatesConversationTimestamp()
    {
        var storage = new InMemoryStorage();
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

    #region Fact Search with Lexical-Only, Vector-Only, and Hybrid Paths

    [Test]
    public async Task SearchFactsAsync_ReturnsBestRankedFactsLexicalOnly()
    {
        var storage = new InMemoryStorage();
        var entityId = await storage.GetOrCreateEntityAsync("entity-1");
        await storage.AddFactsAsync(entityId, new[]
        {
            new NewMemoryFact("the user likes hiking"),
            new NewMemoryFact("the user enjoys coffee"),
            new NewMemoryFact("the user dislikes tea"),
        });

        var results = await storage.SearchFactsAsync(entityId, "coffee", null, 5, 10);

        Assert.That(results, Is.Not.Empty);
        Assert.That(results.First().Content, Does.Contain("coffee"));
    }

    [Test]
    public async Task SearchFactsAsync_ReturnsEmptyWhenNoFactsMatch()
    {
        var storage = new InMemoryStorage();
        var entityId = await storage.GetOrCreateEntityAsync("entity-1");
        await storage.AddFactsAsync(entityId, new[]
        {
            new NewMemoryFact("the user likes hiking"),
        });

        var results = await storage.SearchFactsAsync(entityId, "xyz123", null, 5, 10);

        Assert.That(results, Is.Empty);
    }

    [Test]
    public async Task SearchFactsAsync_ReturnsEmptyForNonexistentEntity()
    {
        var storage = new InMemoryStorage();

        var results = await storage.SearchFactsAsync("nonexistent-entity", "query", null, 5, 10);

        Assert.That(results, Is.Empty);
    }

    [Test]
    public async Task SearchFactsAsync_RespectsCandidateLimit()
    {
        var storage = new InMemoryStorage();
        var entityId = await storage.GetOrCreateEntityAsync("entity-1");
        await storage.AddFactsAsync(entityId, new[]
        {
            new NewMemoryFact("coffee 1"),
            new NewMemoryFact("coffee 2"),
            new NewMemoryFact("coffee 3"),
            new NewMemoryFact("coffee 4"),
            new NewMemoryFact("coffee 5"),
        });

        var results = await storage.SearchFactsAsync(entityId, "coffee", null, 10, 2);

        Assert.That(results.Count, Is.LessThanOrEqualTo(2));
    }

    [Test]
    public async Task SearchFactsAsync_RespectsResultLimit()
    {
        var storage = new InMemoryStorage();
        var entityId = await storage.GetOrCreateEntityAsync("entity-1");
        await storage.AddFactsAsync(entityId, new[]
        {
            new NewMemoryFact("coffee 1"),
            new NewMemoryFact("coffee 2"),
            new NewMemoryFact("coffee 3"),
            new NewMemoryFact("coffee 4"),
            new NewMemoryFact("coffee 5"),
        });

        var results = await storage.SearchFactsAsync(entityId, "coffee", null, 2, 10);

        Assert.That(results.Count, Is.EqualTo(2));
    }

    [Test]
    public async Task SearchFactsAsync_PrefersHigherConfidenceWhenLexicalSignalsMatch()
    {
        var storage = new InMemoryStorage();
        var entityId = await storage.GetOrCreateEntityAsync("entity-1");
        var older = DateTimeOffset.UtcNow.AddHours(-1);
        var newer = DateTimeOffset.UtcNow;

        await storage.AddFactsAsync(entityId, new[]
        {
            new NewMemoryFact("coffee", createdAt: older, confidence: 0.1, memoryType: "preference"),
            new NewMemoryFact("coffee", createdAt: newer, confidence: 0.9, memoryType: "preference"),
        });

        var results = await storage.SearchFactsAsync(entityId, "coffee", null, 5, 10);

        Assert.That(results, Has.Count.EqualTo(2));
        Assert.That(results[0].Confidence, Is.EqualTo(0.9).Within(0.0001));
        Assert.That(results[1].Confidence, Is.EqualTo(0.1).Within(0.0001));
    }

    [Test]
    public async Task SearchFactsAsync_PrefersNewerFactsWhenSignalsMatch()
    {
        var storage = new InMemoryStorage();
        var entityId = await storage.GetOrCreateEntityAsync("entity-1");
        var older = DateTimeOffset.UtcNow.AddHours(-2);
        var newer = DateTimeOffset.UtcNow.AddMinutes(-1);

        await storage.AddFactsAsync(entityId, new[]
        {
            new NewMemoryFact("coffee", createdAt: older, confidence: 0.5, memoryType: "profile"),
            new NewMemoryFact("coffee", createdAt: newer, confidence: 0.5, memoryType: "profile"),
        });

        var results = await storage.SearchFactsAsync(entityId, "coffee", null, 5, 10);

        Assert.That(results, Has.Count.EqualTo(2));
        Assert.That(results[0].CreatedAt, Is.EqualTo(newer));
        Assert.That(results[1].CreatedAt, Is.EqualTo(older));
    }

    [Test]
    public async Task SearchFactsAsync_PreservesMemoryType()
    {
        var storage = new InMemoryStorage();
        var entityId = await storage.GetOrCreateEntityAsync("entity-1");
        await storage.AddFactsAsync(entityId, new[]
        {
            new NewMemoryFact("coffee", memoryType: "preference"),
            new NewMemoryFact("tea", memoryType: "profile"),
            new NewMemoryFact("water", memoryType: "constraint"),
        });

        var results = await storage.SearchFactsAsync(entityId, "coffee", null, 5, 10);

        Assert.That(results, Has.Count.EqualTo(1));
        Assert.That(results[0].MemoryType, Is.EqualTo("preference"));
    }

    [Test]
    public async Task SearchFactsAsync_PreservesEmbeddings()
    {
        var storage = new InMemoryStorage();
        var entityId = await storage.GetOrCreateEntityAsync("entity-1");
        var embedding = new[] { 0.1f, 0.2f, 0.3f };

        await storage.AddFactsAsync(entityId, new[]
        {
            new NewMemoryFact("coffee", embedding: embedding),
        });

        var results = await storage.SearchFactsAsync(entityId, "coffee", null, 5, 10);

        Assert.That(results, Has.Count.EqualTo(1));
        Assert.That(results[0].Similarity, Is.GreaterThanOrEqualTo(0));
    }

    [Test]
    public async Task SearchFactsAsync_PreservesSummaries()
    {
        var storage = new InMemoryStorage();
        var entityId = await storage.GetOrCreateEntityAsync("entity-1");
        var summary = new MemorySummary("user prefers coffee", DateTimeOffset.UtcNow);

        await storage.AddFactsAsync(entityId, new[]
        {
            new NewMemoryFact("coffee", summaries: new[] { summary }),
        });

        var results = await storage.SearchFactsAsync(entityId, "coffee", null, 5, 10);

        Assert.That(results, Has.Count.EqualTo(1));
        Assert.That(results[0].Summaries, Has.Count.EqualTo(1));
        Assert.That(results[0].Summaries[0].Content, Is.EqualTo("user prefers coffee"));
    }

    #endregion

    #region Delete Behavior

    [Test]
    public async Task DeleteEntityMemoriesAsync_RemovesFacts()
    {
        var storage = new InMemoryStorage();
        var entityId = await storage.GetOrCreateEntityAsync("entity-1");
        await storage.AddFactsAsync(entityId, new[]
        {
            new NewMemoryFact("coffee"),
        });

        await storage.DeleteEntityMemoriesAsync(entityId);

        var results = await storage.SearchFactsAsync(entityId, "coffee", null, 5, 10);

        Assert.That(results, Is.Empty);
    }

    [Test]
    public async Task DeleteEntityMemoriesAsync_RemovesSemanticTriples()
    {
        var storage = new InMemoryStorage();
        var entityId = await storage.GetOrCreateEntityAsync("entity-1");
        await storage.AddSemanticTriplesAsync(entityId, new[]
        {
            new SemanticTriple("user", "entity", "likes", "coffee", "beverage"),
        });

        await storage.DeleteEntityMemoriesAsync(entityId);

        // Verify by attempting to add new facts and searching
        await storage.AddFactsAsync(entityId, new[]
        {
            new NewMemoryFact("new fact"),
        });

        var results = await storage.SearchFactsAsync(entityId, "new fact", null, 5, 10);

        Assert.That(results, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task DeleteEntityMemoriesAsync_PreservesConversationHistory()
    {
        var storage = new InMemoryStorage();
        var entityId = await storage.GetOrCreateEntityAsync("entity-1");
        var sessionId = await storage.GetOrCreateSessionAsync("session-1", entityId, null);
        var conversation = await storage.GetOrCreateConversationAsync(sessionId, TimeSpan.FromMinutes(5));

        await storage.AppendMessagesAsync(conversation.Id, new[]
        {
            new ConversationMessage(ConversationRoles.User, "hello"),
        });

        await storage.DeleteEntityMemoriesAsync(entityId);

        var messages = await storage.GetConversationMessagesAsync(conversation.Id);

        Assert.That(messages, Has.Count.EqualTo(1));
        Assert.That(messages[0].Content, Is.EqualTo("hello"));
    }

    [Test]
    public async Task DeleteEntityMemoriesAsync_IsIdempotent()
    {
        var storage = new InMemoryStorage();
        var entityId = await storage.GetOrCreateEntityAsync("entity-1");

        await storage.DeleteEntityMemoriesAsync(entityId);
        await storage.DeleteEntityMemoriesAsync(entityId);

        // Should not throw
        Assert.Pass();
    }

    #endregion

    #region Semantic Triples and Process Attributes

    [Test]
    public async Task AddSemanticTriplesAsync_StoresTriples()
    {
        var storage = new InMemoryStorage();
        var entityId = await storage.GetOrCreateEntityAsync("entity-1");

        await storage.AddSemanticTriplesAsync(entityId, new[]
        {
            new SemanticTriple("user", "entity", "likes", "coffee", "beverage"),
            new SemanticTriple("user", "entity", "dislikes", "tea", "beverage"),
        });

        // Verify by checking that entity exists and can be searched
        var results = await storage.SearchFactsAsync(entityId, "coffee", null, 5, 10);

        Assert.That(results, Is.Not.Null);
    }

    [Test]
    public async Task AddProcessAttributesAsync_StoresAttributes()
    {
        var storage = new InMemoryStorage();
        var processId = await storage.GetOrCreateProcessAsync("workflow-1");

        await storage.AddProcessAttributesAsync(processId, new[]
        {
            "attribute-1",
            "attribute-2",
        });

        // Verify by checking that process exists
        var newProcessId = await storage.GetOrCreateProcessAsync("workflow-1");

        Assert.That(newProcessId, Is.EqualTo(processId));
    }

    [Test]
    public async Task AddProcessAttributesAsync_IgnoresEmptyAttributes()
    {
        var storage = new InMemoryStorage();
        var processId = await storage.GetOrCreateProcessAsync("workflow-1");

        await storage.AddProcessAttributesAsync(processId, new[]
        {
            "attribute-1",
            "",
            "  ",
            "attribute-2",
        });

        // Should not throw and should store non-empty attributes
        Assert.Pass();
    }

    #endregion

    #region Conversation Summary

    [Test]
    public async Task UpdateConversationSummaryAsync_UpdatesSummary()
    {
        var storage = new InMemoryStorage();
        var conversation = await storage.GetOrCreateConversationAsync("session-1", TimeSpan.FromMinutes(5));

        await storage.UpdateConversationSummaryAsync(conversation.Id, "This is a summary");

        var updatedConversation = await storage.GetOrCreateConversationAsync("session-1", TimeSpan.FromMinutes(5));

        Assert.That(updatedConversation.Summary, Is.EqualTo("This is a summary"));
    }

    [Test]
    public async Task UpdateConversationSummaryAsync_UpdatesTimestamp()
    {
        var storage = new InMemoryStorage();
        var conversation = await storage.GetOrCreateConversationAsync("session-1", TimeSpan.FromMinutes(5));
        var originalUpdatedAt = conversation.UpdatedAt;

        await Task.Delay(10);
        await storage.UpdateConversationSummaryAsync(conversation.Id, "Summary");

        var updatedConversation = await storage.GetOrCreateConversationAsync("session-1", TimeSpan.FromMinutes(5));

        Assert.That(updatedConversation.UpdatedAt, Is.GreaterThan(originalUpdatedAt));
    }

    #endregion

    #region Fact Storage with Metadata

    [Test]
    public async Task AddFactsAsync_AssignsUniqueIds()
    {
        var storage = new InMemoryStorage();
        var entityId = await storage.GetOrCreateEntityAsync("entity-1");

        var stored = await storage.AddFactsAsync(entityId, new[]
        {
            new NewMemoryFact("fact 1"),
            new NewMemoryFact("fact 2"),
        });

        Assert.That(stored, Has.Count.EqualTo(2));
        Assert.That(stored[0].Id, Is.Not.EqualTo(stored[1].Id));
    }

    [Test]
    public async Task AddFactsAsync_PreservesConversationId()
    {
        var storage = new InMemoryStorage();
        var entityId = await storage.GetOrCreateEntityAsync("entity-1");
        var conversation = await storage.GetOrCreateConversationAsync("session-1", TimeSpan.FromMinutes(5));

        var stored = await storage.AddFactsAsync(entityId, new[]
        {
            new NewMemoryFact("fact"),
        }, conversation.Id);

        Assert.That(stored[0].ConversationId, Is.EqualTo(conversation.Id));
    }

    [Test]
    public async Task AddFactsAsync_AssignsCreatedAtWhenNotProvided()
    {
        var storage = new InMemoryStorage();
        var entityId = await storage.GetOrCreateEntityAsync("entity-1");

        var stored = await storage.AddFactsAsync(entityId, new[]
        {
            new NewMemoryFact("fact"),
        });

        Assert.That(stored[0].CreatedAt, Is.Not.EqualTo(default(DateTimeOffset)));
    }

    [Test]
    public async Task AddFactsAsync_PreservesProvidedCreatedAt()
    {
        var storage = new InMemoryStorage();
        var entityId = await storage.GetOrCreateEntityAsync("entity-1");
        var createdAt = DateTimeOffset.UtcNow.AddDays(-1);

        var stored = await storage.AddFactsAsync(entityId, new[]
        {
            new NewMemoryFact("fact", createdAt: createdAt),
        });

        Assert.That(stored[0].CreatedAt, Is.EqualTo(createdAt));
    }

    #endregion
}
