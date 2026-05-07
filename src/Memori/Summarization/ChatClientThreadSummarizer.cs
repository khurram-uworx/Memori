using Memori.Models;
using Microsoft.Extensions.AI;
using System.Text;

namespace Memori.Summarization;

/// <summary>
/// Generates thread summaries using an <see cref="IChatClient"/>.
/// </summary>
/// <remarks>
/// This implementation sends conversation messages to the configured chat client
/// with a system prompt requesting a concise summary. It supports both initial
/// summarization and rolling updates that build on a previous summary.
/// </remarks>
public sealed class ChatClientThreadSummarizer : IThreadSummarizer
{
    readonly IChatClient chatClient;
    readonly ThreadSummarizationOptions options;

    /// <summary>
    /// Creates a chat-client-based thread summarizer.
    /// </summary>
    /// <param name="chatClient">The chat client to use for summary generation.</param>
    /// <param name="options">Configuration options. Uses defaults if not provided.</param>
    public ChatClientThreadSummarizer(
        IChatClient chatClient,
        ThreadSummarizationOptions? options = null)
    {
        this.chatClient = chatClient ?? throw new ArgumentNullException(nameof(chatClient));
        this.options = options ?? new ThreadSummarizationOptions();
    }

    /// <inheritdoc />
    public async ValueTask<string> SummarizeAsync(
        IReadOnlyList<ConversationMessage> messages,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);

        var chatMessages = BuildChatMessages(messages, previousSummary: null);
        var response = await chatClient.GetResponseAsync(chatMessages, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return response.Messages?.FirstOrDefault()?.Text?.Trim() ?? string.Empty;
    }

    /// <inheritdoc />
    public async ValueTask<string> SummarizeAsync(
        IReadOnlyList<ConversationMessage> messages,
        string previousSummary,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);

        var chatMessages = BuildChatMessages(messages, previousSummary);
        var response = await chatClient.GetResponseAsync(chatMessages, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return response.Messages?.FirstOrDefault()?.Text?.Trim() ?? string.Empty;
    }

    IReadOnlyList<ChatMessage> BuildChatMessages(
        IReadOnlyList<ConversationMessage> messages,
        string? previousSummary)
    {
        var systemPrompt = new StringBuilder();
        systemPrompt.AppendLine(options.SummaryPrompt);
        systemPrompt.AppendLine();

        if (!string.IsNullOrWhiteSpace(previousSummary))
        {
            systemPrompt.Append(options.PreviousSummaryLabel);
            systemPrompt.AppendLine(":");
            systemPrompt.AppendLine(previousSummary);
            systemPrompt.AppendLine();
        }

        var chatMessages = new List<ChatMessage>
        {
            new ChatMessage(ChatRole.System, systemPrompt.ToString())
        };

        var maxMessages = options.MaxMessagesPerSummary;
        var startIndex = Math.Max(0, messages.Count - maxMessages);
        var count = Math.Min(messages.Count, maxMessages);

        for (int i = startIndex; i < startIndex + count && i < messages.Count; i++)
        {
            var message = messages[i];
            var text = options.IncludeTimestamps
                ? $"[{message.CreatedAt:u}] {message.Content}"
                : message.Content;

            var role = MapRole(message.Role);
            chatMessages.Add(new ChatMessage(role, text));
        }

        return chatMessages;
    }

    static ChatRole MapRole(string role)
    {
        if (string.Equals(role, ConversationRoles.User, StringComparison.OrdinalIgnoreCase))
            return ChatRole.User;

        if (string.Equals(role, ConversationRoles.Assistant, StringComparison.OrdinalIgnoreCase))
            return ChatRole.Assistant;

        return ChatRole.System;
    }
}
