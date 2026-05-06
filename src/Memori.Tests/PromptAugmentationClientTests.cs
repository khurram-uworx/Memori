using Memori.Abstractions;
using Memori.Augmentation;
using Memori.Models;
using Microsoft.Extensions.AI;
using NUnit.Framework;

namespace Memori.Tests;

public class PromptAugmentationClientTests
{
    [Test]
    public void AugmentationMapper_CreatesModelsAndIgnoresEmptyValues()
    {
        var summary = AugmentationMapper.ToSummary("  User likes coffee.  ");
        var fact = AugmentationMapper.ToFact(
            "  The user likes coffee.  ",
            " preference ",
            summaries: summary is null ? null : [summary]);
        var triple = AugmentationMapper.ToSemanticTriple(
            " user ",
            " person ",
            " likes ",
            " coffee ",
            " drink ");
        var attributes = AugmentationMapper.ToProcessAttributes([" support ", "", "support", "triage"]);

        Assert.That(fact, Is.Not.Null);
        Assert.That(fact!.Content, Is.EqualTo("The user likes coffee."));
        Assert.That(fact.MemoryType, Is.EqualTo("preference"));
        Assert.That(fact.Summaries, Has.Count.EqualTo(1));
        Assert.That(triple, Is.Not.Null);
        Assert.That(triple!.ToFactText(), Is.EqualTo("user likes coffee"));
        Assert.That(attributes, Is.EqualTo(["support", "triage"]));
        Assert.That(AugmentationMapper.ToFact(" "), Is.Null);
        Assert.That(AugmentationMapper.ToSemanticTriple("user", "person", "", "coffee", "drink"), Is.Null);
        Assert.That(AugmentationMapper.ToSummary(" "), Is.Null);
    }

    [Test]
    public async Task AugmentAsync_ParsesStructuredJsonOutput()
    {
        var client = new PromptAugmentationClient(
            new RecordingChatClient(
                """
                {
                  "facts": [
                    { "content": "The user likes coffee.", "createdAt": "2026-05-06T12:00:00Z", "memoryType": "preference" }
                  ],
                  "semanticTriples": [
                    {
                      "subjectName": "user",
                      "subjectType": "person",
                      "predicate": "likes",
                      "objectName": "coffee",
                      "objectType": "drink"
                    }
                  ],
                  "processAttributes": [ "support", "triage" ],
                  "conversationSummary": "The user likes coffee."
                }
                """));

        var result = await client.AugmentAsync(
            new AugmentationInput(
                "entity-1",
                "process-1",
                "conversation-1",
                [
                    new ConversationMessage(ConversationRoles.User, "I like coffee."),
                    new ConversationMessage(ConversationRoles.Assistant, "Noted."),
                ]));

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Facts, Has.Count.EqualTo(1));
        Assert.That(result.Facts[0].Content, Is.EqualTo("The user likes coffee."));
        Assert.That(result.Facts[0].MemoryType, Is.EqualTo("preference"));
        Assert.That(result.SemanticTriples, Has.Count.EqualTo(1));
        Assert.That(result.SemanticTriples[0].ToFactText(), Is.EqualTo("user likes coffee"));
        Assert.That(result.ProcessAttributes, Is.EqualTo(new[] { "support", "triage" }));
        Assert.That(result.ConversationSummary, Is.EqualTo("The user likes coffee."));
    }

    [Test]
    public async Task AugmentAsync_ReturnsNullForEmptyOutput()
    {
        var client = new PromptAugmentationClient(new RecordingChatClient(string.Empty));

        var result = await client.AugmentAsync(
            new AugmentationInput(
                "entity-1",
                null,
                "conversation-1",
                [new ConversationMessage(ConversationRoles.User, "hello")]));

        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task AugmentAsync_ReturnsNullForMalformedJson()
    {
        var client = new PromptAugmentationClient(new RecordingChatClient("not json"));

        var result = await client.AugmentAsync(
            new AugmentationInput(
                "entity-1",
                null,
                "conversation-1",
                [new ConversationMessage(ConversationRoles.User, "hello")]));

        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task AugmentAsync_IgnoresFillerValuesInsideJson()
    {
        var client = new PromptAugmentationClient(
            new RecordingChatClient(
                """
                {
                  "facts": [
                    { "content": "   " },
                    { "content": "The user likes coffee.", "memoryType": "profile" }
                  ],
                  "semanticTriples": [
                    {
                      "subjectName": "user",
                      "subjectType": "person",
                      "predicate": "   ",
                      "objectName": "coffee",
                      "objectType": "drink"
                    }
                  ],
                  "processAttributes": [ " ", "triage" ],
                  "conversationSummary": "   "
                }
                """));

        var result = await client.AugmentAsync(
            new AugmentationInput(
                "entity-1",
                null,
                "conversation-1",
                [new ConversationMessage(ConversationRoles.User, "hello")]));

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Facts, Has.Count.EqualTo(1));
        Assert.That(result.Facts[0].Content, Is.EqualTo("The user likes coffee."));
        Assert.That(result.Facts[0].MemoryType, Is.EqualTo("profile"));
        Assert.That(result.SemanticTriples, Is.Null);
        Assert.That(result.ProcessAttributes, Is.EqualTo(new[] { "triage" }));
        Assert.That(result.ConversationSummary, Is.Null);
    }

    sealed class RecordingChatClient : IChatClient
    {
        readonly string responseText;

        public RecordingChatClient(string responseText)
        {
            this.responseText = responseText;
        }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, responseText)));

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            yield break;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        { }
    }
}
