namespace Memori.Models;

/// <summary>
/// Identifies the entity whose memories are being captured and, optionally, the
/// process or workflow that produced them.
/// </summary>
public sealed record Attribution
{
    internal static string ValidateRequiredIdentifier(string value, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Identifier cannot be empty.", paramName);
        }

        if (value.Length > MaxIdentifierLength)
        {
            throw new ArgumentException(
                $"Identifier cannot be greater than {MaxIdentifierLength} characters.",
                paramName);
        }

        return value;
    }

    internal static string? ValidateOptionalIdentifier(string? value, string paramName)
    {
        if (value is null)
        {
            return null;
        }

        return ValidateRequiredIdentifier(value, paramName);
    }

    /// <summary>
    /// The maximum identifier length accepted by Memori attribution fields.
    /// </summary>
    public const int MaxIdentifierLength = 100;

    /// <summary>
    /// Creates an attribution context.
    /// </summary>
    /// <param name="entityId">External entity identifier, usually a user id.</param>
    /// <param name="processId">Optional external process or workflow identifier.</param>
    /// <exception cref="ArgumentException">
    /// Thrown when an identifier is empty or longer than <see cref="MaxIdentifierLength"/>.
    /// </exception>
    public Attribution(string entityId, string? processId = null)
    {
        EntityId = ValidateRequiredIdentifier(entityId, nameof(entityId));
        ProcessId = ValidateOptionalIdentifier(processId, nameof(processId));
    }

    /// <summary>
    /// External entity identifier, usually a user id.
    /// </summary>
    public string EntityId { get; }

    /// <summary>
    /// Optional external process or workflow identifier.
    /// </summary>
    public string? ProcessId { get; }
}
