using Memori.Abstractions;
using Memori.Models;
using System.Globalization;
using System.Text;

namespace Memori.Search;

/// <summary>
/// Coordinates memory recall across embeddings, storage search, relevance filtering, and prompt formatting.
/// </summary>
public sealed class MemorySearchService
{
    static double scoreForThreshold(RecallResult result)
    {
        if (result.RankScore > 0)
            return result.RankScore;

        return result.Similarity > 0 ? result.Similarity : 0;
    }

    static string formatTimestampSuffix(DateTimeOffset createdAt)
        => $". Stated at {createdAt.ToString("u", CultureInfo.InvariantCulture).TrimEnd('Z')}";

    readonly IStorage storage;
    readonly IMemoriEmbeddingGenerator? embeddingGenerator;
    readonly MemoriOptions options;

    /// <summary>
    /// Creates a memory search service.
    /// </summary>
    public MemorySearchService(IStorage storage,
        IMemoriEmbeddingGenerator? embeddingGenerator = null,
        MemoriOptions? options = null)
    {
        this.storage = storage ?? throw new ArgumentNullException(nameof(storage));
        this.embeddingGenerator = embeddingGenerator;
        this.options = options ?? new MemoriOptions();
        this.options.Validate();
    }

    async ValueTask<float[]?> generateQueryEmbeddingAsync(string query, CancellationToken cancellationToken)
    {
        if (embeddingGenerator is null)
            return null;

        var embedding = await embeddingGenerator.GenerateEmbeddingAsync(query, cancellationToken)
            .ConfigureAwait(false);

        return embedding?.ToArray();
    }

    IEnumerable<RecallResult> normalize(IReadOnlyList<RecallResult> results)
        => results
        .Where(result => !string.IsNullOrWhiteSpace(result.Content))
        .OrderByDescending(result => scoreForThreshold(result))
        .ThenByDescending(result => result.CreatedAt);

    bool isRelevant(RecallResult result)
        => scoreForThreshold(result) >= options.RecallRelevanceThreshold;

    /// <summary>
    /// Recalls relevant facts for an entity and query.
    /// </summary>
    public async ValueTask<IReadOnlyList<RecallResult>> RecallAsync(
        string entityId,
        string query,
        int? limit = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var resolvedLimit = limit ?? options.RecallFactsLimit;

        if (resolvedLimit <= 0)
            throw new ArgumentOutOfRangeException(nameof(limit), "Limit must be greater than zero.");

        var queryEmbedding = await generateQueryEmbeddingAsync(query, cancellationToken)
            .ConfigureAwait(false);
        var results = await storage.SearchFactsAsync(
                entityId,
                query,
                queryEmbedding is null ? null : new ReadOnlyMemory<float>(queryEmbedding),
                resolvedLimit,
                Math.Max(options.RecallCandidateLimit, resolvedLimit),
                cancellationToken)
            .ConfigureAwait(false);

        return normalize(results)
            .Where(isRelevant)
            .Take(resolvedLimit)
            .ToArray();
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

            var suffix = options.IncludeFactTimestampsInPrompt
                ? formatTimestampSuffix(result.CreatedAt)
                : string.Empty;
            lines.Add($"- {result.Content}{suffix}");
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
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var summary in results.SelectMany(result => result.Summaries))
        {
            if (string.IsNullOrWhiteSpace(summary.Content))
                continue;

            var key = summary.Content.Trim();
            if (!seen.Add(key))
                continue;

            if (options.IncludeFactTimestampsInPrompt)
                lines.Add($"- [{summary.CreatedAt.ToString("u", CultureInfo.InvariantCulture).TrimEnd('Z')}] {summary.Content}");
            else
                lines.Add($"- {summary.Content}");
        }

        return lines;
    }

    /// <summary>
    /// Formats recalled facts into a delimited context block suitable for prompt injection.
    /// </summary>
    public string FormatPromptContext(IReadOnlyList<RecallResult> results)
    {
        ArgumentNullException.ThrowIfNull(results);
        var factLines = FormatFactLines(results);

        if (factLines.Count == 0)
            return string.Empty;

        var summaries = FormatSummaryLines(results);
        var tagName = options.PromptContextTagName.Trim();
        var builder = new StringBuilder();
        builder.Append('<').Append(tagName).AppendLine(">");
        builder.AppendLine(options.PromptContextInstruction);
        builder.AppendLine(options.PromptFactsHeading);

        foreach (var line in factLines)
            builder.AppendLine(line);

        if (summaries.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("## Summaries");

            foreach (var line in summaries)
                builder.AppendLine(line);
        }

        builder.Append("</").Append(tagName).Append('>');
        return builder.ToString();
    }
}
