using Memori.Abstractions;
using Memori.Models;

namespace Memori.Search;

/// <summary>
/// Default in-process ranker that combines similarity, lexical signal, confidence, and recency.
/// </summary>
public sealed class DefaultMemoryRanker : IMemoryRanker
{
    static double calculateRecencyBoost(DateTimeOffset createdAt, DateTimeOffset now)
    {
        var age = now - createdAt;

        if (age <= TimeSpan.Zero)
            return 0.05;

        var days = Math.Max(0.0, age.TotalDays);
        return 0.1 / (1.0 + days);
    }

    /// <inheritdoc />
    public double Rank(RecallResult result, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(result);

        var baseScore = result.RankScore > 0
            ? result.RankScore
            : result.Similarity > 0
                ? result.Similarity
                : 0;

        var confidenceBoost = Math.Clamp(result.Confidence, 0, 1) * 0.2;
        var recencyBoost = calculateRecencyBoost(result.CreatedAt, now);

        return baseScore + confidenceBoost + recencyBoost;
    }
}
