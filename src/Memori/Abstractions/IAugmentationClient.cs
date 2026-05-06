using Memori.Models;

namespace Memori.Abstractions;

/// <summary>
/// Input provided to augmentation clients.
/// </summary>
public sealed record AugmentationInput(
    string EntityId,
    string? ProcessId,
    string ConversationId,
    IReadOnlyList<ConversationMessage> Messages,
    string? ConversationSummary = null);

/// <summary>
/// Output produced by augmentation clients.
/// </summary>
public sealed record AugmentationResult(
    IReadOnlyList<NewMemoryFact>? Facts = null,
    IReadOnlyList<SemanticTriple>? SemanticTriples = null,
    IReadOnlyList<string>? ProcessAttributes = null,
    string? ConversationSummary = null);

/// <summary>
/// Produces durable memory updates from a captured conversation turn.
/// </summary>
public interface IAugmentationClient
{
    /// <summary>
    /// Generates augmentation output for the supplied conversation context.
    /// </summary>
    /// <param name="context">Captured conversation context.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Generated memory updates, or <see langword="null"/> when no augmentation is available.</returns>
    ValueTask<AugmentationResult?> AugmentAsync(
        AugmentationInput context,
        CancellationToken cancellationToken = default);
}
