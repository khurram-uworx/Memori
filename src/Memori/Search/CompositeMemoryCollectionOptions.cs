namespace Memori.Search;

/// <summary>
/// Strategy for write operations across composite backends.
/// </summary>
public enum CompositeWriteStrategy
{
    /// <summary>
    /// Write to all backends. Failures in individual backends are logged but do not fail the overall operation.
    /// </summary>
    All,

    /// <summary>
    /// Write only to the primary (first) backend. Other backends are read-only.
    /// </summary>
    PrimaryOnly,
}

/// <summary>
/// Options for configuring <see cref="CompositeMemoryCollection"/>.
/// </summary>
public sealed class CompositeMemoryCollectionOptions
{
    /// <summary>
    /// Maximum number of backends to query in parallel. Defaults to the number of available processors.
    /// </summary>
    public int MaxConcurrency { get; set; } = Environment.ProcessorCount;

    /// <summary>
    /// Strategy for write operations. Defaults to <see cref="CompositeWriteStrategy.All"/>.
    /// </summary>
    public CompositeWriteStrategy WriteStrategy { get; set; } = CompositeWriteStrategy.All;

    /// <summary>
    /// Strategy for ranking and merging of search results. Defaults to <see cref="DistributedRankingStrategy.MergeSortByScore"/>.
    /// </summary>
    public DistributedRankingStrategy RankingStrategy { get; set; } = DistributedRankingStrategy.MergeSortByScore;

    /// <summary>
    /// Per-source weight overrides for the <see cref="DistributedRankingStrategy.WeightedScore"/> strategy.
    /// Keys are backend names (collection names), values are weights (default 1.0).
    /// </summary>
    public IReadOnlyDictionary<string, double>? SourceWeights { get; set; }

    /// <summary>
    /// Name prefix for the composite collection. Defaults to "composite".
    /// </summary>
    public string Name { get; set; } = "composite";
}
