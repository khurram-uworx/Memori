namespace Memori.Models;

/// <summary>
/// Represents a ranked recall result returned by memory search.
/// </summary>
public sealed record RecallResult
{
    /// <summary>
    /// Creates a recall result.
    /// </summary>
    public RecallResult(string factId, string content, double similarity, double rankScore, DateTimeOffset createdAt,
        IReadOnlyList<MemorySummary>? summaries = null)
    {
        FactId = RequireNonEmpty(factId, nameof(factId));
        Content = RequireNonEmpty(content, nameof(content));
        Similarity = similarity;
        RankScore = rankScore;
        CreatedAt = createdAt;
        Summaries = summaries ?? Array.Empty<MemorySummary>();
    }

    /// <summary>
    /// Public storage identifier for the fact.
    /// </summary>
    public string FactId { get; }

    /// <summary>
    /// Fact text.
    /// </summary>
    public string Content { get; }

    /// <summary>
    /// Semantic similarity score, when available.
    /// </summary>
    public double Similarity { get; }

    /// <summary>
    /// Final rank score after combining recall signals.
    /// </summary>
    public double RankScore { get; }

    /// <summary>
    /// Time the fact was created.
    /// </summary>
    public DateTimeOffset CreatedAt { get; }

    /// <summary>
    /// Summaries associated with the recalled fact.
    /// </summary>
    public IReadOnlyList<MemorySummary> Summaries { get; }

    private static string RequireNonEmpty(string value, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Value cannot be empty.", paramName);

        return value;
    }
}
