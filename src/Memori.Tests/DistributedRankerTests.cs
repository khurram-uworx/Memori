using Memori.Abstractions;
using Memori.Models;
using Memori.Search;
using NUnit.Framework;

namespace Memori.Tests;

public class DistributedRankerTests
{
    static RecallResult MakeResult(string factId, double similarity, double rankScore, DateTimeOffset createdAt, string sourceName = "", double sourceWeight = 1.0)
        => new(factId, $"content-{factId}", similarity, rankScore, createdAt);

    static DateTimeOffset now = DateTimeOffset.UtcNow;

    #region MergeSortByScore Strategy Tests

    [Test]
    public void MergeSort_SingleSource_ReturnsSourceResultsUnchanged()
    {
        var ranker = new DefaultDistributedRanker(DistributedRankingStrategy.MergeSortByScore);
        var source1 = new[]
        {
            MakeResult("a", 0.9, 0.9, now.AddHours(-1)),
            MakeResult("b", 0.7, 0.7, now.AddHours(-2)),
        };

        var results = ranker.Rank(new[] { source1 }, now);

        Assert.That(results, Has.Count.EqualTo(2));
        Assert.That(results[0].FactId, Is.EqualTo("a"));
        Assert.That(results[1].FactId, Is.EqualTo("b"));
    }

    [Test]
    public void MergeSort_TwoSources_MergesAndSortsByScore()
    {
        var ranker = new DefaultDistributedRanker(DistributedRankingStrategy.MergeSortByScore);
        var source1 = new[]
        {
            MakeResult("a", 0.9, 0.9, now.AddHours(-1)),
            MakeResult("c", 0.5, 0.5, now.AddHours(-3)),
        };
        var source2 = new[]
        {
            MakeResult("b", 0.8, 0.8, now.AddHours(-2)),
            MakeResult("d", 0.3, 0.3, now.AddHours(-4)),
        };

        var results = ranker.Rank(new[] { source1, source2 }, now);

        Assert.That(results, Has.Count.EqualTo(4));
        Assert.That(results[0].FactId, Is.EqualTo("a"));
        Assert.That(results[1].FactId, Is.EqualTo("b"));
        Assert.That(results[2].FactId, Is.EqualTo("c"));
        Assert.That(results[3].FactId, Is.EqualTo("d"));
    }

    [Test]
    public void MergeSort_DuplicateFactIds_Deduplicates()
    {
        var ranker = new DefaultDistributedRanker(DistributedRankingStrategy.MergeSortByScore);
        var source1 = new[] { MakeResult("a", 0.9, 0.9, now) };
        var source2 = new[] { MakeResult("a", 0.95, 0.95, now) };

        var results = ranker.Rank(new[] { source1, source2 }, now);

        Assert.That(results, Has.Count.EqualTo(1));
        Assert.That(results[0].FactId, Is.EqualTo("a"));
    }

    [Test]
    public void MergeSort_EmptySources_ReturnsEmpty()
    {
        var ranker = new DefaultDistributedRanker(DistributedRankingStrategy.MergeSortByScore);

        var results = ranker.Rank(Array.Empty<IReadOnlyList<RecallResult>>(), now);

        Assert.That(results, Is.Empty);
    }

    [Test]
    public void MergeSort_AllEmptySources_ReturnsEmpty()
    {
        var ranker = new DefaultDistributedRanker(DistributedRankingStrategy.MergeSortByScore);
        var source1 = Array.Empty<RecallResult>();
        var source2 = Array.Empty<RecallResult>();

        var results = ranker.Rank(new[] { source1, source2 }, now);

        Assert.That(results, Is.Empty);
    }

    [Test]
    public void MergeSort_UsesRankScoreOverSimilarity_WhenAvailable()
    {
        var ranker = new DefaultDistributedRanker(
            DistributedRankingStrategy.MergeSortByScore,
            memoryRanker: new TestRanker());
        var source1 = new[] { MakeResult("a", 0.3, 0.9, now.AddHours(-1)) };
        var source2 = new[] { MakeResult("b", 0.95, 0.3, now.AddHours(-2)) };

        var results = ranker.Rank(new[] { source1, source2 }, now);

        Assert.That(results[0].FactId, Is.EqualTo("a"));
    }

    #endregion

    #region WeightedScore Strategy Tests

    [Test]
    public void WeightedScore_AppliesWeightsBeforeSorting()
    {
        var weights = new Dictionary<string, double>
        {
            ["source-0"] = 2.0,
            ["source-1"] = 0.5,
        };
        var ranker = new DefaultDistributedRanker(DistributedRankingStrategy.WeightedScore, weights);
        var source1 = new[] { MakeResult("a", 0.4, 0.4, now) };
        var source2 = new[] { MakeResult("b", 0.9, 0.9, now) };

        var results = ranker.Rank(new[] { source1, source2 }, now);

        Assert.That(results[0].FactId, Is.EqualTo("a"));
        Assert.That(results[1].FactId, Is.EqualTo("b"));
    }

    [Test]
    public void WeightedScore_DefaultWeightIsOne()
    {
        var ranker = new DefaultDistributedRanker(DistributedRankingStrategy.WeightedScore);
        var source1 = new[] { MakeResult("a", 0.5, 0.5, now) };
        var source2 = new[] { MakeResult("b", 0.9, 0.9, now) };

        var results = ranker.Rank(new[] { source1, source2 }, now);

        Assert.That(results[0].FactId, Is.EqualTo("b"));
        Assert.That(results[1].FactId, Is.EqualTo("a"));
    }

    [Test]
    public void WeightedScore_DeduplicatesAcrossSources()
    {
        var ranker = new DefaultDistributedRanker(DistributedRankingStrategy.WeightedScore);
        var source1 = new[] { MakeResult("a", 0.9, 0.9, now) };
        var source2 = new[] { MakeResult("a", 0.5, 0.5, now) };

        var results = ranker.Rank(new[] { source1, source2 }, now);

        Assert.That(results, Has.Count.EqualTo(1));
    }

    [Test]
    public void WeightedScore_UsesSimilarity_WhenRankScoreIsZero()
    {
        var ranker = new DefaultDistributedRanker(DistributedRankingStrategy.WeightedScore);
        var source1 = new[] { MakeResult("a", 0.8, 0, now) };

        var results = ranker.Rank(new[] { source1 }, now);

        Assert.That(results, Has.Count.EqualTo(1));
        Assert.That(results[0].FactId, Is.EqualTo("a"));
    }

    #endregion

    #region RoundRobin Strategy Tests

    [Test]
    public void RoundRobin_InterleavesResultsFromSources()
    {
        var ranker = new DefaultDistributedRanker(DistributedRankingStrategy.RoundRobin);
        var source1 = new[]
        {
            MakeResult("a", 0.9, 0.9, now),
            MakeResult("c", 0.7, 0.7, now),
            MakeResult("e", 0.5, 0.5, now),
        };
        var source2 = new[]
        {
            MakeResult("b", 0.85, 0.85, now),
            MakeResult("d", 0.65, 0.65, now),
        };

        var results = ranker.Rank(new[] { source1, source2 }, now);

        Assert.That(results, Has.Count.EqualTo(5));
        Assert.That(results[0].FactId, Is.EqualTo("a"));
        Assert.That(results[1].FactId, Is.EqualTo("b"));
        Assert.That(results[2].FactId, Is.EqualTo("c"));
        Assert.That(results[3].FactId, Is.EqualTo("d"));
        Assert.That(results[4].FactId, Is.EqualTo("e"));
    }

    [Test]
    public void RoundRobin_SkipsDuplicatesWithinSource()
    {
        var ranker = new DefaultDistributedRanker(DistributedRankingStrategy.RoundRobin);
        var source1 = new[]
        {
            MakeResult("a", 0.9, 0.9, now),
            MakeResult("a", 0.8, 0.8, now),
        };
        var source2 = new[] { MakeResult("b", 0.85, 0.85, now) };

        var results = ranker.Rank(new[] { source1, source2 }, now);

        Assert.That(results, Has.Count.EqualTo(2));
    }

    [Test]
    public void RoundRobin_SkipsDuplicatesAcrossSources()
    {
        var ranker = new DefaultDistributedRanker(DistributedRankingStrategy.RoundRobin);
        var source1 = new[] { MakeResult("a", 0.9, 0.9, now), MakeResult("c", 0.7, 0.7, now) };
        var source2 = new[] { MakeResult("a", 0.85, 0.85, now), MakeResult("b", 0.8, 0.8, now) };

        var results = ranker.Rank(new[] { source1, source2 }, now);

        Assert.That(results, Has.Count.EqualTo(3));
        Assert.That(results[0].FactId, Is.EqualTo("a"));
        Assert.That(results[1].FactId, Is.EqualTo("c"));
        Assert.That(results[2].FactId, Is.EqualTo("b"));
    }

    [Test]
    public void RoundRobin_UnevenSources_ContinuesUntilAllExhausted()
    {
        var ranker = new DefaultDistributedRanker(DistributedRankingStrategy.RoundRobin);
        var source1 = new[] { MakeResult("a", 0.9, 0.9, now) };
        var source2 = new[]
        {
            MakeResult("b", 0.85, 0.85, now),
            MakeResult("c", 0.8, 0.8, now),
        };
        var source3 = new[] { MakeResult("d", 0.75, 0.75, now) };

        var results = ranker.Rank(new[] { source1, source2, source3 }, now);

        Assert.That(results, Has.Count.EqualTo(4));
        Assert.That(results[0].FactId, Is.EqualTo("a"));
        Assert.That(results[1].FactId, Is.EqualTo("b"));
        Assert.That(results[2].FactId, Is.EqualTo("d"));
        Assert.That(results[3].FactId, Is.EqualTo("c"));
    }

    [Test]
    public void RoundRobin_EmptySources_ReturnsEmpty()
    {
        var ranker = new DefaultDistributedRanker(DistributedRankingStrategy.RoundRobin);

        var results = ranker.Rank(Array.Empty<IReadOnlyList<RecallResult>>(), now);

        Assert.That(results, Is.Empty);
    }

    #endregion

    #region Edge Cases

    [Test]
    public void ThreeSources_AllStrategies_ProduceValidResults()
    {
        var source1 = new[] { MakeResult("a", 0.9, 0.9, now) };
        var source2 = new[] { MakeResult("b", 0.8, 0.8, now) };
        var source3 = new[] { MakeResult("c", 0.7, 0.7, now) };

        foreach (DistributedRankingStrategy strategy in Enum.GetValues<DistributedRankingStrategy>())
        {
            var ranker = new DefaultDistributedRanker(strategy);
            var results = ranker.Rank(new[] { source1, source2, source3 }, now);
            Assert.That(results, Has.Count.EqualTo(3), $"Strategy {strategy} failed");
        }
    }

    [Test]
    public void SingleResultAcrossMultipleSources_IsIncluded()
    {
        var ranker = new DefaultDistributedRanker(DistributedRankingStrategy.MergeSortByScore);
        var source1 = new[] { MakeResult("a", 0.9, 0.9, now) };
        var source2 = Array.Empty<RecallResult>();
        var source3 = new[] { MakeResult("b", 0.8, 0.8, now) };

        var results = ranker.Rank(new[] { source1, source2, source3 }, now);

        Assert.That(results, Has.Count.EqualTo(2));
    }

    [Test]
    public void DeterministicOutput_SameInput_SameOrder()
    {
        var ranker = new DefaultDistributedRanker(DistributedRankingStrategy.MergeSortByScore);
        var source1 = new[] { MakeResult("a", 0.9, 0.9, now), MakeResult("b", 0.7, 0.7, now) };
        var source2 = new[] { MakeResult("c", 0.8, 0.8, now) };

        var input = new[] { source1, source2 };
        var results1 = ranker.Rank(input, now);
        var results2 = ranker.Rank(input, now);

        Assert.That(results1.Select(r => r.FactId), Is.EqualTo(results2.Select(r => r.FactId)));
    }

    #endregion

    sealed class TestRanker : IMemoryRanker
    {
        public double Rank(RecallResult result, DateTimeOffset now)
            => result.RankScore;
    }
}
