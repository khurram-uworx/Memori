using Memori.Abstractions;
using Memori.Models;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.VectorData;
using System.Globalization;
using System.Linq.Expressions;
using System.Text;

namespace Memori.Search;

/// <summary>
/// Coordinates memory recall across embeddings, VectorStore search, relevance filtering, and prompt formatting.
/// </summary>
public sealed class MemorySearchService
{
    static double scoreForThreshold(RecallResult result)
    {
        if (result.RankScore > 0)
            return result.RankScore;

        return result.Similarity > 0 ? result.Similarity : 0;
    }

    static string renderPromptContext(
        IReadOnlyList<PromptContextFact> facts,
        IReadOnlyList<PromptContextSummary> summaries,
        PromptContextMetadata metadata)
    {
        if (facts.Count == 0)
            return string.Empty;

        var builder = new StringBuilder();
        builder.Append('<').Append(metadata.TagName).AppendLine(">");
        builder.AppendLine(metadata.Instruction);
        builder.AppendLine(metadata.FactsHeading);

        foreach (var fact in facts)
            builder.AppendLine(fact.RenderedText);

        if (summaries.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine(metadata.SummariesHeading);

            foreach (var summary in summaries)
                builder.AppendLine(summary.RenderedText);
        }

        builder.Append("</").Append(metadata.TagName).Append('>');

        return builder.ToString();
    }

    readonly VectorStoreCollection<string, MemoryFactRecord> factCollection;
    readonly IEmbeddingGenerator<string, Embedding<float>>? embeddingGenerator;
    readonly MemoriOptions options;
    readonly IMemoryRanker ranker;

    /// <summary>
    /// Creates a memory search service.
    /// </summary>
    public MemorySearchService(
        VectorStoreCollection<string, MemoryFactRecord> factCollection,
        IEmbeddingGenerator<string, Embedding<float>>? embeddingGenerator = null,
        MemoriOptions? options = null,
        IMemoryRanker? ranker = null)
    {
        this.factCollection = factCollection ?? throw new ArgumentNullException(nameof(factCollection));
        this.embeddingGenerator = embeddingGenerator;
        this.options = options ?? new MemoriOptions();
        this.options.Validate();
        this.ranker = ranker ?? new DefaultMemoryRanker();
    }

    async ValueTask<float[]?> generateQueryEmbeddingAsync(string query, CancellationToken cancellationToken)
    {
        if (embeddingGenerator is null)
            return null;

        var embedding = await embeddingGenerator.GenerateAsync(query, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return embedding.Vector.ToArray();
    }

    IEnumerable<RecallResult> normalize(IReadOnlyList<RecallResult> results)
        => results
        .Where(result => !string.IsNullOrWhiteSpace(result.Content))
        .OrderByDescending(result => ranker.Rank(result, DateTimeOffset.UtcNow))
        .ThenByDescending(result => result.CreatedAt);

    bool isRelevant(RecallResult result)
        => scoreForThreshold(result) >= options.RecallRelevanceThreshold;

    string formatTimestamp(DateTimeOffset createdAt)
        => createdAt.ToString(options.PromptTimestampFormat, CultureInfo.InvariantCulture).TrimEnd('Z');

    string formatTimestampSuffix(DateTimeOffset createdAt)
        => $". Stated at {formatTimestamp(createdAt)}";

    string formatFactLine(RecallResult result)
    {
        var suffix = options.IncludeFactTimestampsInPrompt
            ? formatTimestampSuffix(result.CreatedAt)
            : string.Empty;
        return $"{options.PromptFactBullet.Trim()} {result.Content}{suffix}";
    }

    string formatSummaryLine(MemorySummary summary)
    {
        var timestamp = options.IncludeFactTimestampsInPrompt
            ? $"[{formatTimestamp(summary.CreatedAt)}] "
            : string.Empty;
        return $"{options.PromptSummaryBullet.Trim()} {timestamp}{summary.Content}";
    }

    static Expression<Func<MemoryFactRecord, bool>>? buildRecallFilter(
        string entityId,
        string? scope)
    {
        if (scope is null)
            return r => r.EntityId == entityId && !r.IsDeleted;

        return r => r.EntityId == entityId && r.Scope == scope && !r.IsDeleted;
    }

    /// <summary>
    /// Recalls relevant facts for an entity and query, optionally filtered by scope.
    /// </summary>
    public async ValueTask<IReadOnlyList<RecallResult>> RecallAsync(
        string entityId,
        string query,
        int? limit = null,
        string? scope = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var resolvedLimit = limit ?? options.RecallFactsLimit;

        if (resolvedLimit <= 0)
            throw new ArgumentOutOfRangeException(nameof(limit), "Limit must be greater than zero.");

        var queryEmbedding = await generateQueryEmbeddingAsync(query, cancellationToken)
            .ConfigureAwait(false);

        if (queryEmbedding is not null)
        {
            // Vector search path
            var searchOptions = new VectorSearchOptions<MemoryFactRecord>
            {
                Filter = buildRecallFilter(entityId, scope)
            };

            var searchResults = factCollection.SearchAsync(new ReadOnlyMemory<float>(queryEmbedding), Math.Max(options.RecallCandidateLimit, resolvedLimit), searchOptions, cancellationToken);

            var results = new List<RecallResult>();
            await foreach (var result in searchResults)
            {
                var recallResult = new RecallResult(
                    factId: result.Record.Id,
                    content: result.Record.Content,
                    similarity: result.Score ?? 0,
                    rankScore: result.Score ?? 0,
                    createdAt: result.Record.CreatedAt,
                    summaries: result.Record.Summaries,
                    confidence: (double)result.Record.Confidence,
                    memoryType: result.Record.MemoryType);

                results.Add(recallResult);
            }

            return normalize(results)
                .Where(isRelevant)
                .Take(resolvedLimit)
                .ToArray();
        }
        else
        {
            // Lexical fallback path - get all facts for entity and do local scoring
            var searchOptions = new VectorSearchOptions<MemoryFactRecord>
            {
                Filter = buildRecallFilter(entityId, scope)
            };

            var searchResults = factCollection.SearchAsync(query, Math.Max(options.RecallCandidateLimit, resolvedLimit), searchOptions, cancellationToken);

            var results = new List<RecallResult>();
            await foreach (var result in searchResults)
            {
                var lexicalScore = Similarity.LexicalScore(query, result.Record.Content);
                var recallResult = new RecallResult(
                    factId: result.Record.Id,
                    content: result.Record.Content,
                    similarity: 0,
                    rankScore: lexicalScore,
                    createdAt: result.Record.CreatedAt,
                    summaries: result.Record.Summaries,
                    confidence: (double)result.Record.Confidence,
                    memoryType: result.Record.MemoryType);

                results.Add(recallResult);
            }

            return normalize(results)
                .Where(isRelevant)
                .Take(resolvedLimit)
                .ToArray();
        }
    }

    /// <summary>
    /// Formats recalled facts as markdown-like bullet lines.
    /// </summary>
    public IReadOnlyList<string> FormatFactLines(IReadOnlyList<RecallResult> results)
    {
        ArgumentNullException.ThrowIfNull(results);

        var lines = new List<string>(results.Count);
        foreach (var result in results)
        {
            if (string.IsNullOrWhiteSpace(result.Content))
                continue;

            lines.Add(formatFactLine(result));
        }

        return lines;
    }

    /// <summary>
    /// Formats summaries associated with recalled facts as bullet lines.
    /// </summary>
    public IReadOnlyList<string> FormatSummaryLines(IReadOnlyList<RecallResult> results)
    {
        ArgumentNullException.ThrowIfNull(results);

        var lines = new List<string>();
        if (!options.IncludeSummariesInPrompt)
            return lines;

        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var summary in results.SelectMany(result => result.Summaries))
        {
            if (string.IsNullOrWhiteSpace(summary.Content))
                continue;

            var key = summary.Content.Trim();
            if (!seen.Add(key))
                continue;

            lines.Add(formatSummaryLine(summary));
        }

        return lines;
    }

    /// <summary>
    /// Builds structured prompt context for recalled memories.
    /// </summary>
    public PromptContext BuildPromptContext(IReadOnlyList<RecallResult> results)
    {
        ArgumentNullException.ThrowIfNull(results);

        var facts = new List<PromptContextFact>(results.Count);
        foreach (var result in results)
        {
            if (string.IsNullOrWhiteSpace(result.Content))
                continue;

            facts.Add(new PromptContextFact(
                result.FactId,
                result.Content,
                result.MemoryType,
                result.Confidence,
                result.Similarity,
                result.RankScore,
                result.CreatedAt,
                formatFactLine(result)));
        }

        var summaries = new List<PromptContextSummary>();
        if (options.IncludeSummariesInPrompt)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var summary in results.SelectMany(result => result.Summaries))
            {
                if (string.IsNullOrWhiteSpace(summary.Content))
                    continue;

                var key = summary.Content.Trim();
                if (!seen.Add(key))
                    continue;

                summaries.Add(new PromptContextSummary(
                    summary.Content,
                    summary.CreatedAt,
                    formatSummaryLine(summary)));
            }
        }

        var metadata = new PromptContextMetadata(
            options.PromptContextTagName.Trim(),
            options.PromptContextInstruction,
            options.PromptFactsHeading,
            options.PromptSummariesHeading,
            options.IncludeFactTimestampsInPrompt,
            options.IncludeSummariesInPrompt);

        return new PromptContext(facts, summaries, metadata, renderPromptContext(facts, summaries, metadata));
    }

    /// <summary>
    /// Formats recalled facts into a delimited context block suitable for prompt injection.
    /// </summary>
    public string FormatPromptContext(IReadOnlyList<RecallResult> results)
    {
        ArgumentNullException.ThrowIfNull(results);
        return BuildPromptContext(results).RenderedText;
    }
}
