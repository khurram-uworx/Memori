namespace Memori.Models;

/// <summary>
/// Controls whether recalled memory context is inserted as a new message or merged into an existing message.
/// </summary>
public enum PromptInjectionMergeStrategy
{
    /// <summary>
    /// Always inserts recalled memory context as a separate message.
    /// </summary>
    None,

    /// <summary>
    /// Prepends recalled memory context to the first existing message with the configured injection role.
    /// </summary>
    PrependToFirstMatchingRole,

    /// <summary>
    /// Appends recalled memory context to the last existing message with the configured injection role.
    /// </summary>
    AppendToLastMatchingRole,
}

/// <summary>
/// Controls where recalled memory context is inserted into chat history.
/// </summary>
public enum PromptInjectionPlacement
{
    /// <summary>
    /// Inserts recalled memory context before every existing message.
    /// </summary>
    BeforeAllMessages,

    /// <summary>
    /// Inserts recalled memory context after leading system messages.
    /// </summary>
    AfterSystemMessages,

    /// <summary>
    /// Inserts recalled memory context after leading system and developer messages.
    /// </summary>
    AfterSystemAndDeveloperMessages,

    /// <summary>
    /// Inserts recalled memory context after every existing message.
    /// </summary>
    Append,
}
