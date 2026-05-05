using Memori.Models;

namespace Memori.Abstractions;

/// <summary>
/// Stores and retrieves Memori's durable domain objects.
/// </summary>
/// <remarks>
/// <para>
/// This contract is intentionally domain-oriented. Implementations should hide
/// provider-specific details such as connections, SQL commands, migrations,
/// indexes, and transaction handles.
/// </para>
/// <para>
/// Version 1 does not expose an explicit transaction API. A storage provider may
/// use internal transactions to make each operation atomic, but callers should
/// not need to coordinate transaction state across storage calls.
/// </para>
/// <para>
/// Implementations are expected to be safe for concurrent use by multiple
/// requests. All get-or-create methods must be idempotent for the same logical
/// external identifier.
/// </para>
/// </remarks>
public interface IStorage
{
    /// <summary>
    /// Gets or creates an entity and returns its public storage identifier.
    /// </summary>
    /// <param name="externalId">External entity identifier, usually a user id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The public storage identifier for the entity.</returns>
    ValueTask<string> GetOrCreateEntityAsync(
        string externalId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets or creates a process and returns its public storage identifier.
    /// </summary>
    /// <param name="externalId">External process or workflow identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The public storage identifier for the process.</returns>
    ValueTask<string> GetOrCreateProcessAsync(
        string externalId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets or creates a session and returns its public storage identifier.
    /// </summary>
    /// <param name="sessionId">External session identifier.</param>
    /// <param name="entityId">Public storage identifier for the entity, if available.</param>
    /// <param name="processId">Public storage identifier for the process, if available.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The public storage identifier for the session.</returns>
    ValueTask<string> GetOrCreateSessionAsync(
        string sessionId,
        string? entityId,
        string? processId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the active conversation for a session or creates a new one when the
    /// last conversation is older than <paramref name="timeout"/>.
    /// </summary>
    /// <param name="sessionId">Public storage identifier for the session.</param>
    /// <param name="timeout">Maximum inactivity before a new conversation is created.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The active conversation.</returns>
    ValueTask<Conversation> GetOrCreateConversationAsync(
        string sessionId,
        TimeSpan timeout,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Appends messages to a conversation in the order provided.
    /// </summary>
    /// <param name="conversationId">Public storage identifier for the conversation.</param>
    /// <param name="messages">Messages to append.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask AppendMessagesAsync(
        string conversationId,
        IReadOnlyList<ConversationMessage> messages,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads all messages for a conversation in insertion order.
    /// </summary>
    /// <param name="conversationId">Public storage identifier for the conversation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Conversation messages in insertion order.</returns>
    ValueTask<IReadOnlyList<ConversationMessage>> GetConversationMessagesAsync(
        string conversationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates a conversation summary.
    /// </summary>
    /// <param name="conversationId">Public storage identifier for the conversation.</param>
    /// <param name="summary">Summary text.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask UpdateConversationSummaryAsync(
        string conversationId,
        string summary,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds new facts for an entity.
    /// </summary>
    /// <param name="entityId">Public storage identifier for the entity.</param>
    /// <param name="facts">Facts to add.</param>
    /// <param name="conversationId">Optional public storage identifier for the source conversation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The stored facts, including assigned identifiers.</returns>
    ValueTask<IReadOnlyList<MemoryFact>> AddFactsAsync(
        string entityId,
        IReadOnlyList<NewMemoryFact> facts,
        string? conversationId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Searches facts for an entity.
    /// </summary>
    /// <remarks>
    /// Implementations may use vector search, lexical search, hybrid search, or
    /// any provider-native ranking strategy. Returned results should be ordered
    /// by descending rank score. The first-party in-memory implementation uses
    /// the same public contract as custom providers.
    /// </remarks>
    /// <param name="entityId">Public storage identifier for the entity.</param>
    /// <param name="query">Natural-language query text.</param>
    /// <param name="queryEmbedding">Optional query embedding for semantic search.</param>
    /// <param name="limit">Maximum number of results to return.</param>
    /// <param name="candidateLimit">Maximum number of candidate facts to consider before final ranking.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Ranked recall results.</returns>
    ValueTask<IReadOnlyList<RecallResult>> SearchFactsAsync(
        string entityId,
        string query,
        ReadOnlyMemory<float>? queryEmbedding,
        int limit,
        int candidateLimit,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes durable memories for an entity while preserving conversation history.
    /// </summary>
    /// <param name="entityId">Public storage identifier for the entity.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask DeleteEntityMemoriesAsync(
        string entityId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds semantic triples for an entity.
    /// </summary>
    /// <param name="entityId">Public storage identifier for the entity.</param>
    /// <param name="triples">Semantic triples to add.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask AddSemanticTriplesAsync(
        string entityId,
        IReadOnlyList<SemanticTriple> triples,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds attributes for a process.
    /// </summary>
    /// <param name="processId">Public storage identifier for the process.</param>
    /// <param name="attributes">Attributes to add.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask AddProcessAttributesAsync(
        string processId,
        IReadOnlyList<string> attributes,
        CancellationToken cancellationToken = default);
}
