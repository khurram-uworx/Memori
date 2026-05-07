namespace Memori.Summarization;

/// <summary>
/// Options for configuring thread summarization behavior.
/// </summary>
public sealed class ThreadSummarizationOptions
{
    /// <summary>
    /// The maximum number of messages to include in a single summarization request.
    /// When exceeded, older messages are dropped from the request (the previous
    /// summary carries forward the context).
    /// </summary>
    public int MaxMessagesPerSummary { get; set; } = 50;

    /// <summary>
    /// The system prompt to use when generating summaries via an IChatClient.
    /// </summary>
    public string SummaryPrompt { get; set; } =
        "Summarize the key information from this conversation concisely. " +
        "Capture decisions, preferences, facts, and action items. " +
        "If a previous summary is provided, incorporate it and update with new information.";

    /// <summary>
    /// The label to prepend to the previous summary when generating an updated summary.
    /// </summary>
    public string PreviousSummaryLabel { get; set; } = "Previous summary";

    /// <summary>
    /// Whether to include timestamps when rendering messages for summarization.
    /// </summary>
    public bool IncludeTimestamps { get; set; }

    /// <summary>
    /// The memory type to assign to stored summary records.
    /// </summary>
    public string SummaryMemoryType { get; set; } = "summary";
}
