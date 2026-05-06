namespace Memori.Models;

/// <summary>
/// Request-scoped options that override default Memori behavior for a single chat interaction.
/// </summary>
/// <remarks>
/// These options allow fine-grained control over retrieval and capture behavior on a per-request basis,
/// enabling use cases like recall-only, capture-only, or disabled recall/capture flows.
/// </remarks>
public sealed class MemoriRequestOptions
{
    /// <summary>
    /// When set to false, skips memory recall for this request.
    /// Default is true (recall is enabled).
    /// </summary>
    public bool EnableRecall { get; set; } = true;

    /// <summary>
    /// When set to false, skips conversation capture for this request.
    /// Default is true (capture is enabled).
    /// </summary>
    public bool EnableCapture { get; set; } = true;

    /// <summary>
    /// Overrides the default number of recalled facts for this request.
    /// When null, uses the value from MemoriOptions.RecallFactsLimit.
    /// </summary>
    public int? RecallFactsLimit { get; set; }

    /// <summary>
    /// Validates option values and throws when a value cannot be used safely.
    /// </summary>
    public void Validate()
    {
        if (RecallFactsLimit.HasValue && RecallFactsLimit <= 0)
            throw new InvalidOperationException(
                $"{nameof(RecallFactsLimit)} must be greater than zero when specified.");
    }
}
