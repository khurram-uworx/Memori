namespace Memori.Models;

/// <summary>
/// Represents a subject-predicate-object relationship extracted from conversation memory.
/// </summary>
public sealed record SemanticTriple
{
    static string requireNonEmpty(string value, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Value cannot be empty.", paramName);

        return value;
    }

    /// <summary>
    /// Creates a semantic triple.
    /// </summary>
    public SemanticTriple(string subjectName, string subjectType, string predicate, string objectName, string objectType)
    {
        SubjectName = requireNonEmpty(subjectName, nameof(subjectName));
        SubjectType = requireNonEmpty(subjectType, nameof(subjectType));
        Predicate = requireNonEmpty(predicate, nameof(predicate));
        ObjectName = requireNonEmpty(objectName, nameof(objectName));
        ObjectType = requireNonEmpty(objectType, nameof(objectType));
    }

    /// <summary>
    /// Subject entity name.
    /// </summary>
    public string SubjectName { get; }

    /// <summary>
    /// Subject entity type.
    /// </summary>
    public string SubjectType { get; }

    /// <summary>
    /// Relationship predicate.
    /// </summary>
    public string Predicate { get; }

    /// <summary>
    /// Object entity name.
    /// </summary>
    public string ObjectName { get; }

    /// <summary>
    /// Object entity type.
    /// </summary>
    public string ObjectType { get; }

    /// <summary>
    /// Converts the triple into a fact-like sentence for recall.
    /// </summary>
    public string ToFactText() => $"{SubjectName} {Predicate} {ObjectName}";
}
