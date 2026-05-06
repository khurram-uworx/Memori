namespace Memori.Models;

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
        IReadOnlyList<MemorySummary>? summaries = null,
        double confidence = 0.5,
        string memoryType = "general")
    {
        Id = RequireNonEmpty(id, nameof(id));
        EntityId = Attribution.ValidateRequiredIdentifier(entityId, nameof(entityId));
        Content = RequireNonEmpty(content, nameof(content));
        CreatedAt = createdAt;
        Embedding = embedding;
        ConversationId = conversationId;
        Summaries = summaries ?? Array.Empty<MemorySummary>();
        Confidence = confidence;
        MemoryType = RequireNonEmpty(memoryType, nameof(memoryType));
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

    /// <summary>
    /// Confidence score for the fact.
    /// </summary>
    public double Confidence { get; }

    /// <summary>
    /// Memory category or type, for example <c>preference</c> or <c>profile</c>.
    /// </summary>
    public string MemoryType { get; }

    private static string RequireNonEmpty(string value, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Value cannot be empty.", paramName);

        return value;
    }
}

/// <summary>
/// Represents a fact to add to storage before a storage identifier has been assigned.
/// </summary>
public sealed record NewMemoryFact
{
    /// <summary>
    /// Creates a new fact payload.
    /// </summary>
    public NewMemoryFact(string content,
        IReadOnlyList<float>? embedding = null,
        IReadOnlyList<MemorySummary>? summaries = null,
        DateTimeOffset? createdAt = null,
        double confidence = 0.5,
        string memoryType = "general")
    {
        if (string.IsNullOrWhiteSpace(content))
            throw new ArgumentException("Value cannot be empty.", nameof(content));

        Content = content;
        Embedding = embedding;
        Summaries = summaries ?? Array.Empty<MemorySummary>();
        CreatedAt = createdAt;
        Confidence = confidence;
        MemoryType = requireNonEmpty(memoryType, nameof(memoryType));
    }

    /// <summary>
    /// Fact text.
    /// </summary>
    public string Content { get; }

    /// <summary>
    /// Optional embedding vector for semantic recall.
    /// </summary>
    public IReadOnlyList<float>? Embedding { get; }

    /// <summary>
    /// Summaries associated with this fact.
    /// </summary>
    public IReadOnlyList<MemorySummary> Summaries { get; }

    /// <summary>
    /// Optional creation timestamp supplied by the caller.
    /// </summary>
    public DateTimeOffset? CreatedAt { get; }

    /// <summary>
    /// Confidence score for the fact.
    /// </summary>
    public double Confidence { get; }

    /// <summary>
    /// Memory category or type, for example <c>preference</c> or <c>profile</c>.
    /// </summary>
    public string MemoryType { get; }

    static string requireNonEmpty(string value, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Value cannot be empty.", paramName);

        return value;
    }
}
