namespace Memori.Versioning;

/// <summary>
/// Defines strategies for resolving version conflicts during concurrent memory updates.
/// </summary>
public enum ConflictResolutionStrategy
{
    /// <summary>
    /// The most recent write wins, regardless of version ordering.
    /// This is the safest default for high-throughput scenarios where
    /// occasional data loss from concurrent writes is acceptable.
    /// </summary>
    LastWriteWins,

    /// <summary>
    /// Merges conflicting versions by combining their content when possible.
    /// When content is incompatible, the latest version wins with a merge
    /// note recorded in the audit trail.
    /// </summary>
    Merge,

    /// <summary>
    /// Flags the conflict for manual review. The conflicting record is stored
    /// as a new version with a conflict marker, and no automatic resolution is applied.
    /// A human or external process must resolve the conflict.
    /// </summary>
    Manual,
}
