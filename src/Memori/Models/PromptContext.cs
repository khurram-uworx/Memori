namespace Memori.Models;

/// <summary>
/// A recalled fact prepared for prompt context rendering.
/// </summary>
public sealed record PromptContextFact(
    string FactId,
    string Content,
    string MemoryType,
    double Confidence,
    double Similarity,
    double RankScore,
    DateTimeOffset CreatedAt,
    string RenderedText);

/// <summary>
/// A memory summary prepared for prompt context rendering.
/// </summary>
public sealed record PromptContextSummary(
    string Content,
    DateTimeOffset CreatedAt,
    string RenderedText);

/// <summary>
/// Rendering metadata for a structured memory prompt context.
/// </summary>
public sealed record PromptContextMetadata(
    string TagName,
    string Instruction,
    string FactsHeading,
    string SummariesHeading,
    bool IncludeTimestamps,
    bool IncludeSummaries);

/// <summary>
/// Structured recalled memory context for prompt injection or host rendering.
/// </summary>
public sealed record PromptContext(
    IReadOnlyList<PromptContextFact> Facts,
    IReadOnlyList<PromptContextSummary> Summaries,
    PromptContextMetadata Metadata,
    string RenderedText);
