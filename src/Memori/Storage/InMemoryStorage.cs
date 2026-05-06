using Memori.Abstractions;
using Memori.Models;
using Memori.Search;

namespace Memori.Storage;

/// <summary>
/// Thread-safe in-memory implementation of <see cref="IStorage"/>.
/// </summary>
/// <remarks>
/// This implementation is intended for tests, demos, local development, and as
/// reference behavior for custom storage providers. It is not durable across
/// process restarts.
/// </remarks>
public sealed class InMemoryStorage : IStorage
{
    sealed class EntityState(string id)
    {
        public string Id { get; } = id;

        public List<MemoryFact> Facts { get; } = [];

        public List<SemanticTriple> SemanticTriples { get; } = [];
    }

    sealed class ProcessState(string id)
    {
        public string Id { get; } = id;

        public HashSet<string> Attributes { get; } = new(StringComparer.Ordinal);
    }

    sealed class SessionState(string id, string? entityId, string? processId)
    {
        public string Id { get; } = id;

        public string? EntityId { get; set; } = entityId;

        public string? ProcessId { get; set; } = processId;

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

    ConversationState getConversationState(string conversationId)
    {
        if (!conversations.TryGetValue(conversationId, out var conversation))
            throw new KeyNotFoundException($"Conversation '{conversationId}' was not found.");

        return conversation;
    }

    /// <inheritdoc />
    public ValueTask<string> GetOrCreateEntityAsync(string externalId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var id = Attribution.ValidateRequiredIdentifier(externalId, nameof(externalId));

        lock (gate)
        {
            if (!entities.ContainsKey(id))
                entities[id] = new EntityState(id);

            return ValueTask.FromResult(id);
        }
    }

    /// <inheritdoc />
    public ValueTask<string> GetOrCreateProcessAsync(string externalId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var id = Attribution.ValidateRequiredIdentifier(externalId, nameof(externalId));

        lock (gate)
        {
            if (!processes.ContainsKey(id))
                processes[id] = new ProcessState(id);

            return ValueTask.FromResult(id);
        }
    }

    /// <inheritdoc />
    public ValueTask<string> GetOrCreateSessionAsync(string sessionId, string? entityId, string? processId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var id = requireNonEmpty(sessionId, nameof(sessionId));

        lock (gate)
        {
            if (!sessions.TryGetValue(id, out var session))
            {
                session = new SessionState(id, entityId, processId);
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
                session = new SessionState(id, entityId: null, processId: null);
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

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<MemoryFact>> AddFactsAsync(string entityId, IReadOnlyList<NewMemoryFact> facts,
        string? conversationId = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var id = Attribution.ValidateRequiredIdentifier(entityId, nameof(entityId));
        ArgumentNullException.ThrowIfNull(facts);

        lock (gate)
        {
            if (!entities.TryGetValue(id, out var entity))
            {
                entity = new EntityState(id);
                entities[id] = entity;
            }

            var stored = new List<MemoryFact>(facts.Count);

            foreach (var fact in facts)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ArgumentNullException.ThrowIfNull(fact);

                var memoryFact = new MemoryFact(
                    id: newId("fact"),
                    entityId: entity.Id,
                    content: fact.Content,
                    createdAt: fact.CreatedAt ?? DateTimeOffset.UtcNow,
                    embedding: fact.Embedding?.ToArray(),
                    conversationId: conversationId,
                    summaries: fact.Summaries.ToArray());

                entity.Facts.Add(memoryFact);
                stored.Add(memoryFact);
            }

            return ValueTask.FromResult<IReadOnlyList<MemoryFact>>(stored);
        }
    }

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<RecallResult>> SearchFactsAsync(string entityId, string query,
        ReadOnlyMemory<float>? queryEmbedding, int limit, int candidateLimit,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var id = Attribution.ValidateRequiredIdentifier(entityId, nameof(entityId));

        if (limit <= 0)
            throw new ArgumentOutOfRangeException(nameof(limit), "Limit must be greater than zero.");

        if (candidateLimit <= 0)
            throw new ArgumentOutOfRangeException(nameof(candidateLimit), "Candidate limit must be greater than zero.");

        lock (gate)
        {
            if (!entities.TryGetValue(id, out var entity) || entity.Facts.Count == 0)
                return ValueTask.FromResult<IReadOnlyList<RecallResult>>(Array.Empty<RecallResult>());

            var candidates = entity.Facts
                .Take(candidateLimit)
                .Select(fact =>
                {
                    var similarity = queryEmbedding.HasValue && fact.Embedding is not null
                        ? Similarity.Cosine(queryEmbedding.Value.Span, fact.Embedding)
                        : 0;
                    var hasDenseSignal = queryEmbedding.HasValue && fact.Embedding is not null;
                    var lexicalScore = Similarity.LexicalScore(query, fact.Content);
                    var rankScore = Similarity.RankScore(similarity, lexicalScore, hasDenseSignal);

                    return new RecallResult(
                        fact.Id,
                        fact.Content,
                        similarity,
                        rankScore,
                        fact.CreatedAt,
                        fact.Summaries);
                })
                .Where(result => result.RankScore > 0)
                .OrderByDescending(result => result.RankScore)
                .ThenByDescending(result => result.CreatedAt)
                .Take(limit)
                .ToArray();

            return ValueTask.FromResult<IReadOnlyList<RecallResult>>(candidates);
        }
    }

    /// <inheritdoc />
    public ValueTask DeleteEntityMemoriesAsync(string entityId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var id = Attribution.ValidateRequiredIdentifier(entityId, nameof(entityId));

        lock (gate)
        {
            if (entities.TryGetValue(id, out var entity))
            {
                entity.Facts.Clear();
                entity.SemanticTriples.Clear();
            }
        }

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask AddSemanticTriplesAsync(string entityId, IReadOnlyList<SemanticTriple> triples,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var id = Attribution.ValidateRequiredIdentifier(entityId, nameof(entityId));
        ArgumentNullException.ThrowIfNull(triples);

        lock (gate)
        {
            if (!entities.TryGetValue(id, out var entity))
            {
                entity = new EntityState(id);
                entities[id] = entity;
            }

            entity.SemanticTriples.AddRange(triples);
        }

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask AddProcessAttributesAsync(string processId, IReadOnlyList<string> attributes,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var id = Attribution.ValidateRequiredIdentifier(processId, nameof(processId));
        ArgumentNullException.ThrowIfNull(attributes);

        lock (gate)
        {
            if (!processes.TryGetValue(id, out var process))
            {
                process = new ProcessState(id);
                processes[id] = process;
            }

            foreach (var attribute in attributes)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!string.IsNullOrWhiteSpace(attribute))
                    process.Attributes.Add(attribute);
            }
        }

        return ValueTask.CompletedTask;
    }
}
