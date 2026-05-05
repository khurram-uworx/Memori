using Memori.Models;
using Microsoft.Extensions.AI;
using System.Runtime.CompilerServices;
using System.Text;

namespace Memori.MicrosoftExtensionsAI;

/// <summary>
/// Chat client middleware that injects recalled context and captures completed turns.
/// </summary>
public sealed class MemoriChatClient : DelegatingChatClient
{
    readonly global::Memori.Memori memori;

    /// <summary>
    /// Creates a new Memori chat client wrapper.
    /// </summary>
    public MemoriChatClient(IChatClient innerClient, global::Memori.Memori memori)
        : base(innerClient)
    {
        this.memori = memori ?? throw new ArgumentNullException(nameof(memori));
    }

    /// <inheritdoc />
    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var prepared = await prepareMessagesAsync(messages, options, cancellationToken).ConfigureAwait(false);
        var response = await base.GetResponseAsync(prepared, options, cancellationToken).ConfigureAwait(false);
        await captureAsync(prepared, response, cancellationToken).ConfigureAwait(false);
        return response;
    }

    /// <inheritdoc />
    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var prepared = await prepareMessagesAsync(messages, options, cancellationToken).ConfigureAwait(false);
        var updates = new List<ChatResponseUpdate>();
        await foreach (var update in base.GetStreamingResponseAsync(prepared, options, cancellationToken)
            .WithCancellation(cancellationToken)
            .ConfigureAwait(false))
        {
            updates.Add(update);
            yield return update;
        }

        await captureAsync(prepared, updates, cancellationToken).ConfigureAwait(false);
    }

    async ValueTask<IReadOnlyList<ChatMessage>> prepareMessagesAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(messages);

        var input = messages.ToArray();
        var query = extractLatestUserText(input);
        if (string.IsNullOrWhiteSpace(query))
        {
            return input;
        }

        var recalled = await memori.RecallAsync(query, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (recalled.Count == 0)
        {
            return input;
        }

        var context = memoriSearchContext(recalled);
        var prepared = new List<ChatMessage>(input.Length + 1);
        prepared.AddRange(input);
        prepared.Insert(0, new ChatMessage(ChatRole.System, context));
        return prepared;
    }

    static string extractLatestUserText(IReadOnlyList<ChatMessage> messages)
    {
        for (var i = messages.Count - 1; i >= 0; i--)
        {
            var message = messages[i];
            if (message.Role == ChatRole.User && !string.IsNullOrWhiteSpace(message.Text))
            {
                return message.Text!;
            }
        }

        return string.Empty;
    }

    static string memoriSearchContext(IReadOnlyList<RecallResult> results)
    {
        var builder = new StringBuilder();
        builder.AppendLine("<memori_context>");
        builder.AppendLine("Relevant context about the user:");
        foreach (var result in results)
        {
            if (!string.IsNullOrWhiteSpace(result.Content))
            {
                builder.Append("- ").AppendLine(result.Content);
            }
        }

        builder.Append("</memori_context>");
        return builder.ToString();
    }

    async ValueTask captureAsync(
        IReadOnlyList<ChatMessage> inputMessages,
        ChatResponse response,
        CancellationToken cancellationToken)
    {
        var captured = new List<ConversationMessage>(inputMessages.Count + response.Messages.Count);
        captured.AddRange(inputMessages.Select(toConversationMessage));
        captured.AddRange(response.Messages.Select(toConversationMessage));
        await memori.CaptureAsync(captured, cancellationToken).ConfigureAwait(false);
    }

    async ValueTask captureAsync(
        IReadOnlyList<ChatMessage> inputMessages,
        IReadOnlyList<ChatResponseUpdate> updates,
        CancellationToken cancellationToken)
    {
        if (updates.Count == 0)
        {
            return;
        }

        var messages = new List<ChatMessage>();
        ChatResponseExtensions.AddMessages(messages, updates);
        if (messages.Count == 0)
        {
            return;
        }

        var captured = new List<ConversationMessage>(inputMessages.Count + messages.Count);
        captured.AddRange(inputMessages.Select(toConversationMessage));
        captured.AddRange(messages.Select(toConversationMessage));
        await memori.CaptureAsync(captured, cancellationToken).ConfigureAwait(false);
    }

    static ConversationMessage toConversationMessage(ChatMessage message)
        => new(
            role: message.Role.Value,
            content: message.Text ?? string.Empty,
            createdAt: message.CreatedAt ?? DateTimeOffset.UtcNow,
            metadata: message.AdditionalProperties is null
                ? null
                : new Dictionary<string, object?>(message.AdditionalProperties));
}
