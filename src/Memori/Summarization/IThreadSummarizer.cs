using Memori.Models;

namespace Memori.Summarization;

/// <summary>
/// Generates and manages conversation thread summaries.
/// </summary>
/// <remarks>
/// Thread summarization condenses a sequence of conversation messages into a concise,
/// informative summary that captures key information, decisions, and context.
/// Summaries are stored as <see cref="MemoryFactRecord"/> entries with
/// <see cref="MemoryFactRecord.MemoryType"/> set to <c>"summary"</c>.
/// </remarks>
public interface IThreadSummarizer
{
    /// <summary>
    /// Generates a summary for the given conversation messages.
    /// </summary>
    /// <param name="messages">The conversation messages to summarize.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The generated summary text.</returns>
    ValueTask<string> SummarizeAsync(
        IReadOnlyList<ConversationMessage> messages,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates a summary, incorporating the previous summary for continuity.
    /// </summary>
    /// <param name="messages">The new conversation messages since the last summary.</param>
    /// <param name="previousSummary">The previous summary to build upon.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The updated summary text.</returns>
    ValueTask<string> SummarizeAsync(
        IReadOnlyList<ConversationMessage> messages,
        string previousSummary,
        CancellationToken cancellationToken = default);
}
