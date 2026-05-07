namespace Memori.Search;

/// <summary>
/// Strategy for combining search results from multiple vector store backends.
/// </summary>
public enum DistributedRankingStrategy
{
    /// <summary>
    /// Merges all results from all sources, deduplicates by fact ID, and sorts by score descending.
    /// This is the default strategy and works well when all backends produce comparable scores.
    /// </summary>
    MergeSortByScore,

    /// <summary>
    /// Applies per-source weights to result scores before merging and sorting.
    /// Use this when some backends are more authoritative or reliable than others.
    /// </summary>
    WeightedScore,

    /// <summary>
    /// Interleaves results from each source in round-robin fashion, preserving per-source ordering.
    /// Use this to ensure diversity across backends regardless of score differences.
    /// </summary>
    RoundRobin,
}
