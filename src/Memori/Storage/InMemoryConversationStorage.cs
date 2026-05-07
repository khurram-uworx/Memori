using Memori.Abstractions;
using Memori.Models;

namespace Memori.Storage;

/// <summary>
/// Thread-safe in-memory implementation of <see cref="IConversationStorage"/>.
/// </summary>
/// <remarks>
/// This implementation is intended for tests, demos, local development, and as
/// reference behavior for custom storage providers. It is not durable across
/// process restarts.
/// </remarks>
public sealed class InMemoryConversationStorage : IConversationStorage
{
    sealed class EntityState(string id, string? scope)
    {
        public string Id { get; } = id;

        public string? Scope { get; } = scope;
    }

    sealed class ProcessState(string id, string? scope)
    {
        public string Id { get; } = id;

        public string? Scope { get; } = scope;
    }

    sealed class SessionState(string id, string? entityId, string? processId, string? scope)
    {
        public string Id { get; } = id;

        public string? EntityId { get; set; } = entityId;

        public string? ProcessId { get; set; } = processId;

        public string? Scope { get; } = scope;

        public List<string> ConversationIds { get; } = [];
    }

    sealed class ConversationState(Conversation model)
    {
        public Conversation Model { get; set; } = model;

        public List<ConversationMessage> Messages { get; } = [];
    }

    static string requireNonEmpty(string value, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Value cannot be empty.", paramName);

        return value;
    }

    static string newId(string prefix) => $"{prefix}_{Guid.NewGuid():N}";

    readonly object gate = new();
    readonly Dictionary<string, EntityState> entities = new(StringComparer.Ordinal);
    readonly Dictionary<string, ProcessState> processes = new(StringComparer.Ordinal);
    readonly Dictionary<string, SessionState> sessions = new(StringComparer.Ordinal);
    readonly Dictionary<string, ConversationState> conversations = new(StringComparer.Ordinal);

    /// <summary>
    /// Creates a new in-memory conversation storage instance.
    /// </summary>
    public InMemoryConversationStorage()
    {
    }

    ConversationState getConversationState(string conversationId)
    {
        if (!conversations.TryGetValue(conversationId, out var conversation))
            throw new KeyNotFoundException($"Conversation '{conversationId}' was not found.");

        return conversation;
    }

    /// <inheritdoc />
    public ValueTask<string> GetOrCreateEntityAsync(string externalId,
        string? scope = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var id = Attribution.ValidateRequiredIdentifier(externalId, nameof(externalId));

        lock (gate)
        {
            if (!entities.ContainsKey(id))
                entities[id] = new EntityState(id, scope);

            return ValueTask.FromResult(id);
        }
    }

    /// <inheritdoc />
    public ValueTask<string> GetOrCreateProcessAsync(string externalId,
        string? scope = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var id = Attribution.ValidateRequiredIdentifier(externalId, nameof(externalId));

        lock (gate)
        {
            if (!processes.ContainsKey(id))
                processes[id] = new ProcessState(id, scope);

            return ValueTask.FromResult(id);
        }
    }

    /// <inheritdoc />
    public ValueTask<string> GetOrCreateSessionAsync(string sessionId, string? entityId, string? processId,
        string? scope = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var id = requireNonEmpty(sessionId, nameof(sessionId));

        lock (gate)
        {
            if (!sessions.TryGetValue(id, out var session))
            {
                session = new SessionState(id, entityId, processId, scope);
                sessions[id] = session;
            }
            else
            {
                session.EntityId ??= entityId;
                session.ProcessId ??= processId;
            }

            return ValueTask.FromResult(session.Id);
        }
    }

    /// <inheritdoc />
    public ValueTask<Conversation> GetOrCreateConversationAsync(string sessionId, TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var id = requireNonEmpty(sessionId, nameof(sessionId));

        if (timeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(timeout), "Timeout must be greater than zero.");

        lock (gate)
        {
            if (!sessions.TryGetValue(id, out var session))
            {
                session = new SessionState(id, entityId: null, processId: null, scope: null);
                sessions[id] = session;
            }

            var now = DateTimeOffset.UtcNow;
            var lastConversationId = session.ConversationIds.LastOrDefault();

            if (lastConversationId is not null &&
                conversations.TryGetValue(lastConversationId, out var lastConversation) &&
                now - lastConversation.Model.UpdatedAt <= timeout)

                return ValueTask.FromResult(lastConversation.Model);

            var conversationId = newId("conversation");
            var conversation = new Conversation(
                conversationId,
                session.Id,
                createdAt: now,
                updatedAt: now);

            session.ConversationIds.Add(conversationId);
            conversations[conversationId] = new ConversationState(conversation);

            return ValueTask.FromResult(conversation);
        }
    }

    /// <inheritdoc />
    public ValueTask AppendMessagesAsync(string conversationId, IReadOnlyList<ConversationMessage> messages,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(messages);

        var id = requireNonEmpty(conversationId, nameof(conversationId));

        lock (gate)
        {
            var conversation = getConversationState(id);
            conversation.Messages.AddRange(messages);
            conversation.Model = conversation.Model with { UpdatedAt = DateTimeOffset.UtcNow };
        }

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<ConversationMessage>> GetConversationMessagesAsync(string conversationId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var id = requireNonEmpty(conversationId, nameof(conversationId));

        lock (gate)
        {
            var messages = getConversationState(id).Messages.ToArray();
            return ValueTask.FromResult<IReadOnlyList<ConversationMessage>>(messages);
        }
    }

    /// <inheritdoc />
    public ValueTask UpdateConversationSummaryAsync(string conversationId, string summary,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var id = requireNonEmpty(conversationId, nameof(conversationId));
        ArgumentNullException.ThrowIfNull(summary);

        lock (gate)
        {
            var conversation = getConversationState(id);
            conversation.Model = conversation.Model with
            {
                Summary = summary,
                UpdatedAt = DateTimeOffset.UtcNow,
            };
        }

        return ValueTask.CompletedTask;
    }
}
