namespace Memori.Models;

/// <summary>
/// Represents a message captured from a conversation turn.
/// </summary>
public sealed record ConversationMessage
{
    static string requireNonEmpty(string value, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Value cannot be empty.", paramName);

        return value;
    }

    static readonly IReadOnlyDictionary<string, object?> EmptyMetadata = new Dictionary<string, object?>();

    /// <summary>
    /// Creates a conversation message.
    /// </summary>
    public ConversationMessage(string role, string content,
        string type = ConversationMessageTypes.Text,
        DateTimeOffset? createdAt = null,
        IReadOnlyDictionary<string, object?>? metadata = null)
    {
        Role = requireNonEmpty(role, nameof(role));
        Content = content ?? throw new ArgumentNullException(nameof(content));
        Type = requireNonEmpty(type, nameof(type));
        CreatedAt = createdAt ?? DateTimeOffset.UtcNow;
        Metadata = metadata ?? EmptyMetadata;
    }

    /// <summary>
    /// Message role, for example <c>system</c>, <c>user</c>, <c>assistant</c>, or <c>tool</c>.
    /// </summary>
    public string Role { get; }

    /// <summary>
    /// Message content as text.
    /// </summary>
    public string Content { get; }

    /// <summary>
    /// Message content type.
    /// </summary>
    public string Type { get; }

    /// <summary>
    /// Time the message was captured.
    /// </summary>
    public DateTimeOffset CreatedAt { get; }

    /// <summary>
    /// Optional provider or application metadata associated with the message.
    /// </summary>
    public IReadOnlyDictionary<string, object?> Metadata { get; }
}

/// <summary>
/// Common conversation message content types.
/// </summary>
public static class ConversationMessageTypes
{
    /// <summary>
    /// Plain text content.
    /// </summary>
    public const string Text = "text";
}

/// <summary>
/// Common conversation message roles.
/// </summary>
public static class ConversationRoles
{
    /// <summary>
    /// System instruction role.
    /// </summary>
    public const string System = "system";

    /// <summary>
    /// User message role.
    /// </summary>
    public const string User = "user";

    /// <summary>
    /// Assistant message role.
    /// </summary>
    public const string Assistant = "assistant";

    /// <summary>
    /// Tool message role.
    /// </summary>
    public const string Tool = "tool";
}
