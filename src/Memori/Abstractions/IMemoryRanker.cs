using Memori.Models;

namespace Memori.Abstractions;

/// <summary>
/// Ranks recalled memory candidates for a query.
/// </summary>
public interface IMemoryRanker
{
    /// <summary>
    /// Produces a final rank score for a candidate memory result.
    /// </summary>
    double Rank(RecallResult result, DateTimeOffset now);
}
