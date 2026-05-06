using Memori.Models;
using Microsoft.Extensions.AI;
using System.Runtime.CompilerServices;

namespace Memori;

/// <summary>
/// Chat client middleware that injects recalled context and captures completed turns.
/// </summary>
/// <remarks>
/// Request-scoped behavior can be controlled by passing a <see cref="MemoriRequestOptions"/>
/// instance in <see cref="ChatOptions.AdditionalProperties"/> with the key
/// <see cref="MemoriRequestOptionsKey"/>.
/// </remarks>
public sealed class MemoriChatClient : DelegatingChatClient
{
    static MemoriRequestOptions extractRequestOptions(ChatOptions? options)
    {
        if (options?.AdditionalProperties is null)
            return new MemoriRequestOptions();

        if (options.AdditionalProperties.TryGetValue(MemoriRequestOptionsKey, out var value) &&
            value is MemoriRequestOptions requestOptions)
        {
            requestOptions.Validate();
            return requestOptions;
        }

        return new MemoriRequestOptions();
    }

    static string extractLatestUserText(IReadOnlyList<ChatMessage> messages)
    {
        for (var i = messages.Count - 1; i >= 0; i--)
        {
            var message = messages[i];

            if (message.Role == ChatRole.User && !string.IsNullOrWhiteSpace(message.Text))
                return message.Text!;
        }

        return string.Empty;
    }

    static ConversationMessage toConversationMessage(ChatMessage message)
        => new(
            role: message.Role.Value,
            content: message.Text ?? string.Empty,
            createdAt: message.CreatedAt ?? DateTimeOffset.UtcNow,
            metadata: message.AdditionalProperties is null
                ? null
                : new Dictionary<string, object?>(message.AdditionalProperties));

    /// <summary>
    /// Key used to pass <see cref="MemoriRequestOptions"/> in <see cref="ChatOptions.AdditionalProperties"/>.
    /// </summary>
    public const string MemoriRequestOptionsKey = "memori_request_options";

    readonly Memori memori;

    /// <summary>
    /// Creates a new Memori chat client wrapper.
    /// </summary>
    public MemoriChatClient(IChatClient innerClient, Memori memori) : base(innerClient)
    {
        this.memori = memori ?? throw new ArgumentNullException(nameof(memori));
    }

    async ValueTask<IReadOnlyList<ChatMessage>> prepareMessagesAsync(
        IReadOnlyList<ChatMessage> input,
        MemoriRequestOptions requestOptions,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);

        if (!requestOptions.EnableRecall)
            return input;

        var query = extractLatestUserText(input);
        if (string.IsNullOrWhiteSpace(query))
            return input;

        var limit = requestOptions.RecallFactsLimit;
        var recalled = await memori.RecallAsync(query, limit, cancellationToken).ConfigureAwait(false);
        if (recalled.Count == 0)
            return input;

        var context = memori.FormatPromptContext(recalled);
        var prepared = new List<ChatMessage>(input.Count + 1);

        prepared.AddRange(input);
        prepared.Insert(0, new ChatMessage(ChatRole.System, context));

        return prepared;
    }

    async ValueTask captureAsync(
        IReadOnlyList<ChatMessage> inputMessages,
        ChatResponse response,
        MemoriRequestOptions requestOptions,
        CancellationToken cancellationToken)
    {
        if (!requestOptions.EnableCapture)
            return;

        var captured = new List<ConversationMessage>(inputMessages.Count + response.Messages.Count);
        captured.AddRange(inputMessages.Select(toConversationMessage));
        captured.AddRange(response.Messages.Select(toConversationMessage));
        await memori.CaptureAsync(captured, cancellationToken).ConfigureAwait(false);
    }

    async ValueTask captureAsync(
        IReadOnlyList<ChatMessage> inputMessages,
        IReadOnlyList<ChatResponseUpdate> updates,
        MemoriRequestOptions requestOptions,
        CancellationToken cancellationToken)
    {
        if (!requestOptions.EnableCapture)
            return;

        if (updates.Count == 0)
            return;

        var messages = new List<ChatMessage>();
        ChatResponseExtensions.AddMessages(messages, updates);
        if (messages.Count == 0)
            return;

        var captured = new List<ConversationMessage>(inputMessages.Count + messages.Count);
        captured.AddRange(inputMessages.Select(toConversationMessage));
        captured.AddRange(messages.Select(toConversationMessage));
        await memori.CaptureAsync(captured, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var input = messages.ToArray();
        var requestOptions = extractRequestOptions(options);
        var prepared = await prepareMessagesAsync(input, requestOptions, cancellationToken).ConfigureAwait(false);
        var response = await base.GetResponseAsync(prepared, options, cancellationToken).ConfigureAwait(false);
        await captureAsync(input, response, requestOptions, cancellationToken).ConfigureAwait(false);

        return response;
    }

    /// <inheritdoc />
    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var input = messages.ToArray();
        var requestOptions = extractRequestOptions(options);
        var prepared = await prepareMessagesAsync(input, requestOptions, cancellationToken).ConfigureAwait(false);
        var updates = new List<ChatResponseUpdate>();
        await foreach (var update in base.GetStreamingResponseAsync(prepared, options, cancellationToken)
            .WithCancellation(cancellationToken)
            .ConfigureAwait(false))
        {
            updates.Add(update);
            yield return update;
        }

        await captureAsync(input, updates, requestOptions, cancellationToken).ConfigureAwait(false);
    }
}
