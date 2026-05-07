using Memori.Abstractions;
using Memori.Models;

namespace Memori.Search;

/// <summary>
/// Default distributed ranker that supports merge-sort, weighted, and round-robin strategies.
/// </summary>
public sealed class DefaultDistributedRanker : IDistributedRanker
{
    readonly DistributedRankingStrategy strategy;
    readonly IReadOnlyDictionary<string, double> sourceWeights;
    readonly IMemoryRanker? memoryRanker;

    /// <summary>
    /// Creates a distributed ranker with the specified strategy.
    /// </summary>
    /// <param name="strategy">The combining strategy to use.</param>
    /// <param name="sourceWeights">Per-source weight overrides for the WeightedScore strategy.</param>
    /// <param name="memoryRanker">Optional ranker for post-combining score adjustments. Uses DefaultMemoryRanker if not provided.</param>
    public DefaultDistributedRanker(
        DistributedRankingStrategy strategy = DistributedRankingStrategy.MergeSortByScore,
        IReadOnlyDictionary<string, double>? sourceWeights = null,
        IMemoryRanker? memoryRanker = null)
    {
        this.strategy = strategy;
        this.sourceWeights = sourceWeights ?? new Dictionary<string, double>(0);
        this.memoryRanker = memoryRanker;
    }

    /// <inheritdoc />
    public IReadOnlyList<RecallResult> Rank(IReadOnlyList<IReadOnlyList<RecallResult>> sourceResults, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(sourceResults);

        if (sourceResults.Count == 0)
            return Array.Empty<RecallResult>();

        if (sourceResults.Count == 1)
            return sourceResults[0].ToArray();

        return strategy switch
        {
            DistributedRankingStrategy.MergeSortByScore => RankByMergeSort(sourceResults, now),
            DistributedRankingStrategy.WeightedScore => RankByWeightedScore(sourceResults, now),
            DistributedRankingStrategy.RoundRobin => RankByRoundRobin(sourceResults),
            _ => RankByMergeSort(sourceResults, now),
        };
    }

    IReadOnlyList<RecallResult> RankByMergeSort(IReadOnlyList<IReadOnlyList<RecallResult>> sourceResults, DateTimeOffset now)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var merged = new List<RecallResult>();

        foreach (var sourceList in sourceResults)
        {
            foreach (var result in sourceList)
            {
                if (seen.Add(result.FactId))
                    merged.Add(result);
            }
        }

        return ApplyMemoryRanker(merged, now);
    }

    IReadOnlyList<RecallResult> RankByWeightedScore(IReadOnlyList<IReadOnlyList<RecallResult>> sourceResults, DateTimeOffset now)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var weighted = new List<(RecallResult Result, double WeightedScore)>();

        for (int sourceIndex = 0; sourceIndex < sourceResults.Count; sourceIndex++)
        {
            var sourceName = $"source-{sourceIndex}";
            var weight = GetWeight(sourceName);

            foreach (var result in sourceResults[sourceIndex])
            {
                if (!seen.Add(result.FactId))
                    continue;

                var baseScore = result.RankScore > 0 ? result.RankScore : result.Similarity > 0 ? result.Similarity : 0;
                var weightedScore = baseScore * weight;

                var adjusted = new RecallResult(
                    factId: result.FactId,
                    content: result.Content,
                    similarity: result.Similarity,
                    rankScore: weightedScore,
                    createdAt: result.CreatedAt,
                    summaries: result.Summaries,
                    confidence: result.Confidence,
                    memoryType: result.MemoryType);

                weighted.Add((adjusted, weightedScore));
            }
        }

        return weighted
            .OrderByDescending(entry => entry.WeightedScore)
            .Select(entry => entry.Result)
            .ToArray();
    }

    IReadOnlyList<RecallResult> RankByRoundRobin(IReadOnlyList<IReadOnlyList<RecallResult>> sourceResults)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<RecallResult>();

        var iterators = sourceResults
            .Select(list => list.GetEnumerator())
            .ToList();

        bool hasMore;
        do
        {
            hasMore = false;
            for (int i = 0; i < iterators.Count; i++)
            {
                if (iterators[i].MoveNext())
                {
                    hasMore = true;
                    var current = iterators[i].Current;
                    if (seen.Add(current.FactId))
                        result.Add(current);
                }
            }
        }
        while (hasMore);

        foreach (var iterator in iterators)
            iterator.Dispose();

        return result;
    }

    double GetWeight(string sourceName)
    {
        if (sourceWeights.TryGetValue(sourceName, out var weight))
            return weight;
        return 1.0;
    }

    IReadOnlyList<RecallResult> ApplyMemoryRanker(IReadOnlyList<RecallResult> results, DateTimeOffset now)
    {
        if (memoryRanker is null)
            return results
                .OrderByDescending(r => r.RankScore > 0 ? r.RankScore : r.Similarity)
                .ThenByDescending(r => r.CreatedAt)
                .ToArray();

        return results
            .OrderByDescending(r => memoryRanker.Rank(r, now))
            .ThenByDescending(r => r.CreatedAt)
            .ToArray();
    }
}
