using Memori.Models;

namespace Memori.Augmentation;

/// <summary>
/// Helper methods for mapping custom extraction output into Memori augmentation models.
/// </summary>
public static class AugmentationMapper
{
    /// <summary>
    /// Creates a memory fact, returning <see langword="null"/> for empty content.
    /// </summary>
    public static NewMemoryFact? ToFact(
        string? content,
        string? memoryType = null,
        double confidence = 0.5,
        DateTimeOffset? createdAt = null,
        IReadOnlyList<MemorySummary>? summaries = null,
        float[]? embedding = null)
        => string.IsNullOrWhiteSpace(content)
            ? null
            : new NewMemoryFact(
                content.Trim(),
                embedding,
                summaries,
                createdAt,
                confidence,
                string.IsNullOrWhiteSpace(memoryType) ? "general" : memoryType.Trim());

    /// <summary>
    /// Creates a semantic triple, returning <see langword="null"/> when any required part is empty.
    /// </summary>
    public static SemanticTriple? ToSemanticTriple(
        string? subjectName,
        string? subjectType,
        string? predicate,
        string? objectName,
        string? objectType)
        => string.IsNullOrWhiteSpace(subjectName) ||
           string.IsNullOrWhiteSpace(subjectType) ||
           string.IsNullOrWhiteSpace(predicate) ||
           string.IsNullOrWhiteSpace(objectName) ||
           string.IsNullOrWhiteSpace(objectType)
            ? null
            : new SemanticTriple(
                subjectName.Trim(),
                subjectType.Trim(),
                predicate.Trim(),
                objectName.Trim(),
                objectType.Trim());

    /// <summary>
    /// Creates a memory summary, returning <see langword="null"/> for empty content.
    /// </summary>
    public static MemorySummary? ToSummary(string? content, DateTimeOffset createdAt)
        => string.IsNullOrWhiteSpace(content)
            ? null
            : new MemorySummary(content.Trim(), createdAt);

    /// <summary>
    /// Creates a memory summary, returning <see langword="null"/> for empty content.
    /// </summary>
    public static MemorySummary? ToSummary(string? content)
        => ToSummary(content, DateTimeOffset.UtcNow);

    /// <summary>
    /// Normalizes process attributes by trimming empty values and removing duplicates.
    /// </summary>
    public static IReadOnlyList<string> ToProcessAttributes(IEnumerable<string?> attributes)
    {
        ArgumentNullException.ThrowIfNull(attributes);

        return attributes
            .Where(attribute => !string.IsNullOrWhiteSpace(attribute))
            .Select(attribute => attribute!.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }
}
