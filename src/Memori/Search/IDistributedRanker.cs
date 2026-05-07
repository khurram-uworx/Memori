using Memori.Models;

namespace Memori.Search;

/// <summary>
/// Represents a recall result tagged with its originating source for distributed ranking.
/// </summary>
public sealed record SourceTaggedResult
{
    /// <summary>
    /// Creates a source-tagged result.
    /// </summary>
    /// <param name="sourceName">The name of the source backend this result came from.</param>
    /// <param name="result">The recall result.</param>
    /// <param name="sourceWeight">The weight to apply to this source's scores (default 1.0).</param>
    public SourceTaggedResult(string sourceName, RecallResult result, double sourceWeight = 1.0)
    {
        SourceName = sourceName ?? throw new ArgumentNullException(nameof(sourceName));
        Result = result ?? throw new ArgumentNullException(nameof(result));
        SourceWeight = sourceWeight;
    }

    /// <summary>
    /// The name of the source backend this result came from.
    /// </summary>
    public string SourceName { get; }

    /// <summary>
    /// The recall result from this source.
    /// </summary>
    public RecallResult Result { get; }

    /// <summary>
    /// The weight to apply to this source's scores when using weighted ranking.
    /// </summary>
    public double SourceWeight { get; }
}

/// <summary>
/// Ranks and combines recall results from multiple vector store backends.
/// </summary>
public interface IDistributedRanker
{
    /// <summary>
    /// Combines and ranks results from multiple sources into a single ordered list.
    /// </summary>
    /// <param name="sourceResults">Results from each source, each list ordered by that source's ranking.</param>
    /// <param name="now">The current timestamp used for recency calculations.</param>
    /// <returns>A combined, deduplicated, and ranked list of recall results.</returns>
    IReadOnlyList<RecallResult> Rank(IReadOnlyList<IReadOnlyList<RecallResult>> sourceResults, DateTimeOffset now);
}
