namespace Memori.Models;

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
        DateTimeOffset? createdAt = null)
    {
        if (string.IsNullOrWhiteSpace(content))
            throw new ArgumentException("Value cannot be empty.", nameof(content));

        Content = content;
        Embedding = embedding;
        Summaries = summaries ?? Array.Empty<MemorySummary>();
        CreatedAt = createdAt;
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
}
