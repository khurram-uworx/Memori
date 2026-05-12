using Memori.Abstractions;
using Memori.Management;
using Memori.Models;
using Memori.Storage;
using Microsoft.Extensions.VectorData;
using NUnit.Framework;

namespace Memori.Tests;

public class StressTests
{
    sealed class NoOpAugmentationClient : IAugmentationClient
    {
        public ValueTask<AugmentationResult?> AugmentAsync(
            AugmentationInput context,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult<AugmentationResult?>(new AugmentationResult(Facts: []));
    }

    [Test]
    public async Task ConcurrentCaptureAsync_DoesNotThrow()
    {
        var memori = TestMemoriFactory.Create(augmentationClient: new NoOpAugmentationClient());
        memori.Attribution("entity-1");
        memori.SetSession("test-session");

        var tasks = new List<Task>();
        for (var i = 0; i < 20; i++)
        {
            var index = i;
            tasks.Add(Task.Run(async () =>
            {
                await memori.CaptureAsync([
                    new ConversationMessage(ConversationRoles.User, $"message {index}"),
                    new ConversationMessage(ConversationRoles.Assistant, $"response {index}"),
                ]);
            }));
        }

        await Task.WhenAll(tasks);
        await memori.WaitForAugmentationAsync();

        var conversationStorage = GetConversationStorage(memori);
        var conversation = await conversationStorage.GetOrCreateConversationAsync("test-session", TimeSpan.FromMinutes(30));
        var messages = await conversationStorage.GetConversationMessagesAsync(conversation.Id);

        Assert.That(messages, Is.Not.Empty);
        Assert.That(messages.Count, Is.EqualTo(40));
    }

    [Test]
    public async Task ConcurrentCaptureAndRecallAsync_DoesNotThrow()
    {
        var memori = TestMemoriFactory.Create(augmentationClient: new NoOpAugmentationClient());
        var factCollection = GetFactCollection(memori);
        memori.Attribution("entity-1");
        memori.SetSession("test-session");

        // Seed some facts
        for (var i = 0; i < 5; i++)
        {
            await factCollection.UpsertAsync(new MemoryFactRecord
            {
                Id = Guid.NewGuid().ToString("N"),
                EntityId = "entity-1",
                Content = $"seed fact {i}",
                Embedding = new ReadOnlyMemory<float>(new float[1536]),
            });
        }

        var tasks = new List<Task>();
        for (var i = 0; i < 10; i++)
        {
            var index = i;
            tasks.Add(Task.Run(async () =>
            {
                await memori.CaptureAsync([
                    new ConversationMessage(ConversationRoles.User, $"concurrent message {index}"),
                    new ConversationMessage(ConversationRoles.Assistant, $"concurrent response {index}"),
                ]);
            }));
            tasks.Add(Task.Run(async () =>
            {
                var results = await memori.RecallAsync($"seed fact {index % 5}");
                Assert.That(results, Is.Not.Null);
            }));
        }

        await Task.WhenAll(tasks);
        await memori.WaitForAugmentationAsync();
    }

    [Test]
    public async Task ConcurrentMemoryManagementOperations_DoNotThrow()
    {
        var vectorStore = new InMemoryVectorStore();
        var factCollection = vectorStore.GetCollection<string, MemoryFactRecord>("memori_facts");
        var conversationStorage = new InMemoryConversationStorage();
        var management = new MemoryManagementService(factCollection);
        var memori = new MemoriEngine(
            conversationStorage,
            factCollection,
            memoryManagement: management);
        memori.Attribution("entity-1");

        // Seed facts
        for (var i = 0; i < 10; i++)
        {
            await factCollection.UpsertAsync(new MemoryFactRecord
            {
                Id = $"stress-fact-{i}",
                EntityId = "entity-1",
                Content = $"stress memory {i}",
                MemoryType = "stress",
            });
        }

        var tasks = new List<Task>();
        tasks.Add(Task.Run(async () =>
        {
            for (var i = 0; i < 5; i++)
            {
                var count = await memori.GetMemoryCountAsync();
                Assert.That(count, Is.GreaterThanOrEqualTo(0));
            }
        }));
        tasks.Add(Task.Run(async () =>
        {
            var listed = await memori.ListMemoriesAsync(take: 100);
            Assert.That(listed, Is.Not.Empty);
        }));
        tasks.Add(Task.Run(async () =>
        {
            var searched = await memori.SearchMemoriesAsync("stress");
            Assert.That(searched, Is.Not.Empty);
        }));
        tasks.Add(Task.Run(async () =>
        {
            var deleted = await memori.SoftDeleteMemoryAsync("stress-fact-0");
            Assert.That(deleted, Is.True);
        }));
        tasks.Add(Task.Run(async () =>
        {
            var restored = await memori.RestoreMemoryAsync("stress-fact-0");
            Assert.That(restored, Is.True);
        }));

        await Task.WhenAll(tasks);
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
}
