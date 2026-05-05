namespace Memori.Models;

/// <summary>
/// Represents a durable fact remembered for an entity.
/// </summary>
public sealed record MemoryFact
{
    /// <summary>
    /// Creates a memory fact.
    /// </summary>
    public MemoryFact(string id, string entityId, string content, DateTimeOffset createdAt,
        IReadOnlyList<float>? embedding = null,
        string? conversationId = null,
        IReadOnlyList<MemorySummary>? summaries = null)
    {
        Id = RequireNonEmpty(id, nameof(id));
        EntityId = Attribution.ValidateRequiredIdentifier(entityId, nameof(entityId));
        Content = RequireNonEmpty(content, nameof(content));
        CreatedAt = createdAt;
        Embedding = embedding;
        ConversationId = conversationId;
        Summaries = summaries ?? Array.Empty<MemorySummary>();
    }

    /// <summary>
    /// Public storage identifier for the fact.
    /// </summary>
    public string Id { get; }

    /// <summary>
    /// External entity identifier this fact belongs to.
    /// </summary>
    public string EntityId { get; }

    /// <summary>
    /// Fact text.
    /// </summary>
    public string Content { get; }

    /// <summary>
    /// Optional embedding vector for semantic recall.
    /// </summary>
    public IReadOnlyList<float>? Embedding { get; }

    /// <summary>
    /// Optional conversation identifier that produced the fact.
    /// </summary>
    public string? ConversationId { get; }

    /// <summary>
    /// Time the fact was created.
    /// </summary>
    public DateTimeOffset CreatedAt { get; }

    /// <summary>
    /// Summaries associated with this fact.
    /// </summary>
    public IReadOnlyList<MemorySummary> Summaries { get; }

    private static string RequireNonEmpty(string value, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Value cannot be empty.", paramName);
        }

        return value;
    }
}

/// <summary>
/// Represents a summary attached to one or more memories.
/// </summary>
public sealed record MemorySummary
{
    /// <summary>
    /// Creates a memory summary.
    /// </summary>
    public MemorySummary(string content, DateTimeOffset createdAt)
    {
        if (string.IsNullOrWhiteSpace(content))
            throw new ArgumentException("Value cannot be empty.", nameof(content));

        Content = content;
        CreatedAt = createdAt;
    }

    /// <summary>
    /// Summary text.
    /// </summary>
    public string Content { get; }

    /// <summary>
    /// Time the summary was created.
    /// </summary>
    public DateTimeOffset CreatedAt { get; }
}
