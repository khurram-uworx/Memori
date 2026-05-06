namespace Memori.Models;

/// <summary>
/// Configures core Memori behavior.
/// </summary>
public sealed class MemoriOptions
{
    /// <summary>
    /// Default number of recalled facts to return or inject.
    /// </summary>
    public int RecallFactsLimit { get; set; } = 5;

    /// <summary>
    /// Number of candidate facts storage should consider before final ranking.
    /// </summary>
    public int RecallCandidateLimit { get; set; } = 1_000;

    /// <summary>
    /// Minimum final rank score required for a fact to be considered relevant.
    /// </summary>
    public double RecallRelevanceThreshold { get; set; } = 0.1;

    /// <summary>
    /// Amount of inactivity after which a new conversation should be opened for the session.
    /// </summary>
    public TimeSpan SessionTimeout { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>
    /// Whether completed turns should be sent through the augmentation pipeline.
    /// </summary>
    public bool EnableAugmentation { get; set; } = true;

    /// <summary>
    /// Whether augmentation should run in the background instead of blocking model responses.
    /// </summary>
    public bool RunAugmentationInBackground { get; set; } = true;

    /// <summary>
    /// Whether system messages should be omitted from durable conversation capture.
    /// </summary>
    public bool StripSystemMessagesOnCapture { get; set; } = true;

    /// <summary>
    /// Whether recalled fact timestamps should be included in injected prompt context.
    /// </summary>
    public bool IncludeFactTimestampsInPrompt { get; set; } = true;

    /// <summary>
    /// XML-like tag name used to delimit injected memory context.
    /// </summary>
    public string PromptContextTagName { get; set; } = "memori_context";

    /// <summary>
    /// Instruction text prepended to recalled memories in the injected prompt context.
    /// </summary>
    public string PromptContextInstruction { get; set; } =
        "Only use the relevant context if it is relevant to the user's query.";

    /// <summary>
    /// Heading used before recalled facts in the injected prompt context.
    /// </summary>
    public string PromptFactsHeading { get; set; } = "Relevant context about the user:";

    /// <summary>
    /// Validates option values and throws when a value cannot be used safely.
    /// </summary>
    public void Validate()
    {
        if (RecallFactsLimit <= 0)
            throw new InvalidOperationException(
                $"{nameof(RecallFactsLimit)} must be greater than zero.");

        if (RecallCandidateLimit <= 0)
            throw new InvalidOperationException(
                $"{nameof(RecallCandidateLimit)} must be greater than zero.");

        if (RecallCandidateLimit < RecallFactsLimit)
            throw new InvalidOperationException(
                $"{nameof(RecallCandidateLimit)} cannot be less than {nameof(RecallFactsLimit)}.");

        if (RecallRelevanceThreshold is < 0 or > 1)
            throw new InvalidOperationException(
                $"{nameof(RecallRelevanceThreshold)} must be between 0 and 1.");

        if (SessionTimeout <= TimeSpan.Zero)
            throw new InvalidOperationException(
                $"{nameof(SessionTimeout)} must be greater than zero.");

        if (string.IsNullOrWhiteSpace(PromptContextTagName))
            throw new InvalidOperationException(
                $"{nameof(PromptContextTagName)} cannot be empty.");
    }
}
