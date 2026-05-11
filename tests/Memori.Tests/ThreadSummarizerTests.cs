using Memori.Models;
using Memori.Summarization;
using Microsoft.Extensions.AI;
using NUnit.Framework;

namespace Memori.Tests;

public sealed class ThreadSummarizerTests
{
    static readonly ChatMessage SummaryMessage = new(ChatRole.Assistant, "The user discussed their coffee preferences.");

    sealed class FixedResponseChatClient : IChatClient
    {
        readonly ChatResponse response;

        public FixedResponseChatClient(ChatResponse response)
        {
            this.response = response;
        }

        public IReadOnlyList<ChatMessage>? LastRequest { get; private set; }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            LastRequest = messages.ToArray();
            return Task.FromResult(response);
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            LastRequest = messages.ToArray();
            return AsyncEnumerable.Empty<ChatResponseUpdate>();
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose() { }
    }

    static ChatClientThreadSummarizer CreateSummarizer(
        ChatResponse? response = null,
        ThreadSummarizationOptions? options = null)
    {
        var chatClient = new FixedResponseChatClient(
            response ?? new ChatResponse(new ChatMessage(ChatRole.Assistant, "summary text")));
        return new ChatClientThreadSummarizer(chatClient, options);
    }

    [Test]
    public async Task SummarizeAsync_WithMessages_ReturnsSummary()
    {
        var summarizer = CreateSummarizer();
        var messages = new List<ConversationMessage>
        {
            new(ConversationRoles.User, "I like coffee"),
            new(ConversationRoles.Assistant, "Noted."),
        };

        var summary = await summarizer.SummarizeAsync(messages);

        Assert.That(summary, Is.Not.Empty);
    }

    [Test]
    public async Task SummarizeAsync_IncludesSystemPrompt()
    {
        var chatClient = new FixedResponseChatClient(
            new ChatResponse(new ChatMessage(ChatRole.Assistant, "summary")));
        var summarizer = new ChatClientThreadSummarizer(chatClient);
        var messages = new List<ConversationMessage>
        {
            new(ConversationRoles.User, "hello"),
        };

        await summarizer.SummarizeAsync(messages);

        Assert.That(chatClient.LastRequest, Is.Not.Null);
        Assert.That(chatClient.LastRequest!.First().Role, Is.EqualTo(ChatRole.System));
        Assert.That(chatClient.LastRequest!.First().Text, Does.Contain("Summarize"));
    }

    [Test]
    public async Task SummarizeAsync_WithPreviousSummary_IncludesItInPrompt()
    {
        var chatClient = new FixedResponseChatClient(
            new ChatResponse(new ChatMessage(ChatRole.Assistant, "updated summary")));
        var summarizer = new ChatClientThreadSummarizer(chatClient);
        var messages = new List<ConversationMessage>
        {
            new(ConversationRoles.User, "more info"),
        };

        await summarizer.SummarizeAsync(messages, "User likes coffee");

        var systemMessage = chatClient.LastRequest!.First();
        Assert.That(systemMessage.Text, Does.Contain("User likes coffee"));
        Assert.That(systemMessage.Text, Does.Contain("Previous summary"));
    }

    [Test]
    public async Task SummarizeAsync_RespectsMaxMessagesOption()
    {
        var options = new ThreadSummarizationOptions { MaxMessagesPerSummary = 2 };
        var chatClient = new FixedResponseChatClient(
            new ChatResponse(new ChatMessage(ChatRole.Assistant, "summary")));
        var summarizer = new ChatClientThreadSummarizer(chatClient, options);
        var messages = new List<ConversationMessage>
        {
            new(ConversationRoles.User, "msg1"),
            new(ConversationRoles.User, "msg2"),
            new(ConversationRoles.User, "msg3"),
        };

        await summarizer.SummarizeAsync(messages);

        var userMessages = chatClient.LastRequest!.Where(m => m.Role == ChatRole.User).ToList();
        Assert.That(userMessages, Has.Count.EqualTo(2));
        Assert.That(userMessages.First().Text, Is.EqualTo("msg2"));
    }

    [Test]
    public async Task SummarizeAsync_WithEmptyMessages_ReturnsEmpty()
    {
        var response = new ChatResponse(new ChatMessage(ChatRole.Assistant, ""));
        var summarizer = CreateSummarizer(response);

        var summary = await summarizer.SummarizeAsync(Array.Empty<ConversationMessage>());

        Assert.That(summary, Is.Empty);
    }

    [Test]
    public async Task SummarizeAsync_PreservesMessageRoles()
    {
        var chatClient = new FixedResponseChatClient(
            new ChatResponse(new ChatMessage(ChatRole.Assistant, "summary")));
        var summarizer = new ChatClientThreadSummarizer(chatClient);
        var messages = new List<ConversationMessage>
        {
            new(ConversationRoles.User, "user text"),
            new(ConversationRoles.Assistant, "assistant text"),
        };

        await summarizer.SummarizeAsync(messages);

        var userMsgs = chatClient.LastRequest!.Where(m => m.Role == ChatRole.User).ToList();
        var assistantMsgs = chatClient.LastRequest!.Where(m => m.Role == ChatRole.Assistant).ToList();
        Assert.That(userMsgs, Has.Count.EqualTo(1));
        Assert.That(assistantMsgs, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task SummarizeAsync_WithTimestampsEnabled_IncludesTimestamps()
    {
        var options = new ThreadSummarizationOptions { IncludeTimestamps = true };
        var chatClient = new FixedResponseChatClient(
            new ChatResponse(new ChatMessage(ChatRole.Assistant, "summary")));
        var summarizer = new ChatClientThreadSummarizer(chatClient, options);
        var messages = new List<ConversationMessage>
        {
            new(ConversationRoles.User, "hello", createdAt: new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero)),
        };

        await summarizer.SummarizeAsync(messages);

        var userMsg = chatClient.LastRequest!.First(m => m.Role == ChatRole.User);
        Assert.That(userMsg.Text, Does.Contain("2025"));
    }

    [Test]
    public async Task SummarizeAsync_TrimsResponse()
    {
        var response = new ChatResponse(new ChatMessage(ChatRole.Assistant, "  trimmed  "));
        var summarizer = CreateSummarizer(response);
        var messages = new List<ConversationMessage>
        {
            new(ConversationRoles.User, "hello"),
        };

        var summary = await summarizer.SummarizeAsync(messages);

        Assert.That(summary, Is.EqualTo("trimmed"));
    }

    [Test]
    public void Constructor_WithNullChatClient_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new ChatClientThreadSummarizer(null!));
    }

    [Test]
    public async Task SummarizeAsync_WithNullMessages_Throws()
    {
        var summarizer = CreateSummarizer();
        Assert.ThrowsAsync<ArgumentNullException>(() =>
            summarizer.SummarizeAsync(null!).AsTask());
    }
}

file static class AsyncEnumerable
{
    public static async IAsyncEnumerable<T> Empty<T>()
    {
        await Task.CompletedTask;
        yield break;
    }
}
