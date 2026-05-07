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

    static ConversationMessage toConversationMessage(
        ChatMessage message,
        IReadOnlyDictionary<string, object?>? providerMetadata = null)
    {
        var metadata = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (message.AdditionalProperties is not null)
            foreach (var property in message.AdditionalProperties)
                metadata[property.Key] = property.Value;

        if (providerMetadata is not null)
            foreach (var property in providerMetadata)
                metadata[property.Key] = property.Value;

        return new ConversationMessage(
            role: message.Role.Value,
            content: message.Text ?? string.Empty,
            createdAt: message.CreatedAt ?? DateTimeOffset.UtcNow,
            metadata: metadata.Count == 0 ? null : metadata);
    }

    static ConversationMessage toConversationMessage(ChatMessage message)
        => new(
            role: message.Role.Value,
            content: message.Text ?? string.Empty,
            createdAt: message.CreatedAt ?? DateTimeOffset.UtcNow,
            metadata: message.AdditionalProperties is null
                ? null
                : new Dictionary<string, object?>(message.AdditionalProperties));

    static void addIfNotNull(IDictionary<string, object?> metadata, string key, object? value)
    {
        if (value is not null)
            metadata[key] = value;
    }

    static IReadOnlyDictionary<string, object?> getResponseMetadata(ChatResponse response)
    {
        var metadata = new Dictionary<string, object?>(StringComparer.Ordinal);
        addIfNotNull(metadata, "memori.provider.response_id", response.ResponseId);
        addIfNotNull(metadata, "memori.provider.conversation_id", response.ConversationId);
        addIfNotNull(metadata, "memori.provider.model_id", response.ModelId);
        addIfNotNull(metadata, "memori.provider.created_at", response.CreatedAt);
        addIfNotNull(metadata, "memori.provider.finish_reason", response.FinishReason?.ToString());
        addIfNotNull(metadata, "memori.provider.usage", response.Usage);

        if (response.AdditionalProperties is not null && response.AdditionalProperties.Count > 0)
            metadata["memori.provider.response_additional_properties"] =
                new Dictionary<string, object?>(response.AdditionalProperties);

        return metadata;
    }

    static IReadOnlyDictionary<string, object?> getStreamingResponseMetadata(
        ChatResponse response,
        IReadOnlyList<ChatResponseUpdate> updates)
    {
        var metadata = new Dictionary<string, object?>(getResponseMetadata(response), StringComparer.Ordinal);
        var updateResponseIds = updates
            .Select(update => update.ResponseId)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var updateMessageIds = updates
            .Select(update => update.MessageId)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (updateResponseIds.Length > 0)
            metadata["memori.provider.streaming_response_ids"] = updateResponseIds;

        if (updateMessageIds.Length > 0)
            metadata["memori.provider.streaming_message_ids"] = updateMessageIds;

        return metadata;
    }

    static IReadOnlyList<ConversationMessage> applyCapturePolicy(
        IEnumerable<ConversationMessage> messages,
        MemoriOptions options)
    {
        var captured = new List<ConversationMessage>();
        var excludedRoles = options.ExcludedCaptureRoles.Count == 0
            ? null
            : new HashSet<string>(options.ExcludedCaptureRoles, StringComparer.OrdinalIgnoreCase);

        foreach (var message in messages)
        {
            if (excludedRoles?.Contains(message.Role) is true)
                continue;

            if (options.DropEmptyMessagesOnCapture && string.IsNullOrWhiteSpace(message.Content))
                continue;

            if (options.CaptureMessageFilter is not null && !options.CaptureMessageFilter(message))
                continue;

            var transformed = options.CaptureMessageTransform is null
                ? message
                : options.CaptureMessageTransform(message);
            if (transformed is null)
                continue;

            if (options.DropEmptyMessagesOnCapture && string.IsNullOrWhiteSpace(transformed.Content))
                continue;

            captured.Add(transformed);
        }

        return captured;
    }

    static ChatRole toChatRole(string role)
    {
        if (string.Equals(role, ChatRole.System.Value, StringComparison.OrdinalIgnoreCase))
            return ChatRole.System;

        if (string.Equals(role, ChatRole.User.Value, StringComparison.OrdinalIgnoreCase))
            return ChatRole.User;

        if (string.Equals(role, ChatRole.Assistant.Value, StringComparison.OrdinalIgnoreCase))
            return ChatRole.Assistant;

        if (string.Equals(role, ChatRole.Tool.Value, StringComparison.OrdinalIgnoreCase))
            return ChatRole.Tool;

        return new ChatRole(role);
    }

    static int getInsertionIndex(
        IReadOnlyList<ChatMessage> messages,
        PromptInjectionPlacement placement)
    {
        return placement switch
        {
            PromptInjectionPlacement.BeforeAllMessages => 0,
            PromptInjectionPlacement.Append => messages.Count,
            PromptInjectionPlacement.AfterSystemMessages => countLeadingRoles(
                messages,
                static role => role == ChatRole.System),
            PromptInjectionPlacement.AfterSystemAndDeveloperMessages => countLeadingRoles(
                messages,
                static role =>
                    role == ChatRole.System ||
                    string.Equals(role.Value, "developer", StringComparison.OrdinalIgnoreCase)),
            _ => throw new InvalidOperationException(
                $"{nameof(PromptInjectionPlacement)} must be a defined value."),
        };
    }

    static int countLeadingRoles(
        IReadOnlyList<ChatMessage> messages,
        Func<ChatRole, bool> predicate)
    {
        var count = 0;
        foreach (var message in messages)
        {
            if (!predicate(message.Role))
                break;

            count++;
        }

        return count;
    }

    static ChatMessage mergeMessage(ChatMessage message, string content, bool prepend)
    {
        var existingContent = message.Text ?? string.Empty;
        var mergedContent = prepend
            ? string.Concat(content, Environment.NewLine, Environment.NewLine, existingContent)
            : string.Concat(existingContent, Environment.NewLine, Environment.NewLine, content);

        return new ChatMessage(message.Role, mergedContent)
        {
            AdditionalProperties = message.AdditionalProperties,
            AuthorName = message.AuthorName,
            CreatedAt = message.CreatedAt,
            MessageId = message.MessageId,
            RawRepresentation = message.RawRepresentation,
        };
    }

    static bool tryMergePromptContext(
        List<ChatMessage> messages,
        ChatRole injectionRole,
        string context,
        PromptInjectionMergeStrategy mergeStrategy)
    {
        if (mergeStrategy == PromptInjectionMergeStrategy.None)
            return false;

        if (mergeStrategy == PromptInjectionMergeStrategy.PrependToFirstMatchingRole)
        {
            for (var i = 0; i < messages.Count; i++)
            {
                if (messages[i].Role != injectionRole)
                    continue;

                messages[i] = mergeMessage(messages[i], context, prepend: true);
                return true;
            }
        }

        if (mergeStrategy == PromptInjectionMergeStrategy.AppendToLastMatchingRole)
        {
            for (var i = messages.Count - 1; i >= 0; i--)
            {
                if (messages[i].Role != injectionRole)
                    continue;

                messages[i] = mergeMessage(messages[i], context, prepend: false);
                return true;
            }
        }

        return false;
    }

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

        if (!memori.Options.EnablePromptInjection)
            return input;

        var query = extractLatestUserText(input);
        if (string.IsNullOrWhiteSpace(query))
            return input;

        var limit = requestOptions.RecallFactsLimit;
        var recalled = await memori.RecallAsync(query, limit, cancellationToken).ConfigureAwait(false);
        if (recalled.Count == 0)
            return input;

        var context = memori.FormatPromptContext(recalled);
        var prepared = new List<ChatMessage>(input);
        var role = toChatRole(memori.Options.PromptInjectionRole);

        if (!tryMergePromptContext(
            prepared,
            role,
            context,
            memori.Options.PromptInjectionMergeStrategy))
        {
            var index = getInsertionIndex(prepared, memori.Options.PromptInjectionPlacement);
            prepared.Insert(index, new ChatMessage(role, context));
        }

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

        var responseMetadata = getResponseMetadata(response);
        var converted = inputMessages
            .Select(toConversationMessage)
            .Concat(response.Messages.Select(message => toConversationMessage(message, responseMetadata)));
        var captured = applyCapturePolicy(converted, memori.Options);
        if (captured.Count == 0)
            return;

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

        var response = updates.ToChatResponse();
        if (response.Messages.Count == 0)
            return;

        var responseMetadata = getStreamingResponseMetadata(response, updates);
        var converted = inputMessages
            .Select(toConversationMessage)
            .Concat(response.Messages.Select(message => toConversationMessage(message, responseMetadata)));
        var captured = applyCapturePolicy(converted, memori.Options);
        if (captured.Count == 0)
            return;

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
