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
    public double RecallRelevanceThreshold { get; set; } = 0.15;

    /// <summary>
    /// When <see langword="true"/>, <see cref="RecallRelevanceThreshold"/> is effectively set to 0,
    /// bypassing relevance filtering. Useful for debugging and development.
    /// </summary>
    public bool RelaxedMode { get; set; }

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
    /// Whether empty messages should be omitted from durable conversation capture.
    /// </summary>
    public bool DropEmptyMessagesOnCapture { get; set; }

    /// <summary>
    /// Message roles that should be omitted from durable conversation capture after provider messages are converted.
    /// </summary>
    public IList<string> ExcludedCaptureRoles { get; } = [];

    /// <summary>
    /// Optional predicate that can omit converted conversation messages from durable capture.
    /// </summary>
    public Func<ConversationMessage, bool>? CaptureMessageFilter { get; set; }

    /// <summary>
    /// Optional transform that can redact or replace converted conversation messages before durable capture.
    /// Return <see langword="null"/> to omit the message.
    /// </summary>
    public Func<ConversationMessage, ConversationMessage?>? CaptureMessageTransform { get; set; }

    /// <summary>
    /// Whether recalled fact timestamps should be included in injected prompt context.
    /// </summary>
    public bool IncludeFactTimestampsInPrompt { get; set; } = true;

    /// <summary>
    /// Whether memory summaries should be included in injected prompt context.
    /// </summary>
    public bool IncludeSummariesInPrompt { get; set; } = true;

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
    /// Heading used before memory summaries in the injected prompt context.
    /// </summary>
    public string PromptSummariesHeading { get; set; } = "## Summaries";

    /// <summary>
    /// Prefix used when rendering each recalled fact line.
    /// </summary>
    public string PromptFactBullet { get; set; } = "-";

    /// <summary>
    /// Prefix used when rendering each memory summary line.
    /// </summary>
    public string PromptSummaryBullet { get; set; } = "-";

    /// <summary>
    /// Timestamp format used for facts and summaries when timestamps are included.
    /// </summary>
    public string PromptTimestampFormat { get; set; } = "u";

    /// <summary>
    /// Whether recalled memory should be injected into chat requests.
    /// </summary>
    public bool EnablePromptInjection { get; set; } = true;

    /// <summary>
    /// Role used for the injected memory context message.
    /// </summary>
    public string PromptInjectionRole { get; set; } = "system";

    /// <summary>
    /// Where recalled memory context should be inserted into chat history.
    /// </summary>
    public PromptInjectionPlacement PromptInjectionPlacement { get; set; } =
        PromptInjectionPlacement.AfterSystemMessages;

    /// <summary>
    /// Whether recalled memory context should be merged into an existing instruction message.
    /// </summary>
    public PromptInjectionMergeStrategy PromptInjectionMergeStrategy { get; set; } =
        PromptInjectionMergeStrategy.None;

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

        if (string.IsNullOrWhiteSpace(PromptFactBullet))
            throw new InvalidOperationException(
                $"{nameof(PromptFactBullet)} cannot be empty.");

        if (string.IsNullOrWhiteSpace(PromptSummaryBullet))
            throw new InvalidOperationException(
                $"{nameof(PromptSummaryBullet)} cannot be empty.");

        if (string.IsNullOrWhiteSpace(PromptTimestampFormat))
            throw new InvalidOperationException(
                $"{nameof(PromptTimestampFormat)} cannot be empty.");

        if (ExcludedCaptureRoles.Any(string.IsNullOrWhiteSpace))
            throw new InvalidOperationException(
                $"{nameof(ExcludedCaptureRoles)} cannot contain empty roles.");

        if (string.IsNullOrWhiteSpace(PromptInjectionRole))
            throw new InvalidOperationException(
                $"{nameof(PromptInjectionRole)} cannot be empty.");

        if (!Enum.IsDefined(PromptInjectionPlacement))
            throw new InvalidOperationException(
                $"{nameof(PromptInjectionPlacement)} must be a defined value.");

        if (!Enum.IsDefined(PromptInjectionMergeStrategy))
            throw new InvalidOperationException(
                $"{nameof(PromptInjectionMergeStrategy)} must be a defined value.");
    }
}
