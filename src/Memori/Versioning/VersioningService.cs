using Memori.Models;

namespace Memori.Versioning;

/// <summary>
/// Represents the outcome of a conflict resolution attempt.
/// </summary>
public sealed record ConflictResolution(
    MemoryFactRecord ResolvedRecord,
    ConflictResolutionStrategy StrategyUsed,
    string? PreviousContent = null,
    int? PreviousVersion = null,
    bool ConflictDetected = false);

/// <summary>
/// Detects version conflicts and resolves them according to a configurable strategy.
/// </summary>
/// <remarks>
/// The service works with <see cref="MemoryFactRecord"/> version metadata. Each record carries
/// a <see cref="MemoryFactRecord.Version"/> integer and an optional <see cref="MemoryFactRecord.PreviousVersionId"/>
/// that links to the prior version. Conflict detection compares the expected version
/// (the version the caller last read) against the current version in storage.
/// </remarks>
public sealed class VersioningService
{
    readonly ConflictResolutionStrategy defaultStrategy;

    /// <summary>
    /// Creates a versioning service with the specified default resolution strategy.
    /// </summary>
    /// <param name="defaultStrategy">The default strategy when no strategy is specified. Defaults to <see cref="ConflictResolutionStrategy.LastWriteWins"/>.</param>
    public VersioningService(
        ConflictResolutionStrategy defaultStrategy = ConflictResolutionStrategy.LastWriteWins)
    {
        this.defaultStrategy = defaultStrategy;
    }

    /// <summary>
    /// Detects whether a conflict exists between the caller's expected version and the current stored version.
    /// </summary>
    /// <param name="expectedVersion">The version the caller last read.</param>
    /// <param name="currentRecord">The current record in storage, or null if no record exists.</param>
    /// <returns>True when a conflict is detected; false when the versions are compatible.</returns>
    public bool DetectConflict(int expectedVersion, MemoryFactRecord? currentRecord)
    {
        if (currentRecord is null)
            return false;

        return currentRecord.Version != expectedVersion;
    }

    /// <summary>
    /// Resolves a version conflict between the caller's update and the current stored record.
    /// </summary>
    /// <param name="incomingRecord">The record the caller wants to write.</param>
    /// <param name="currentRecord">The current record in storage, or null if this is a new insert.</param>
    /// <param name="expectedVersion">The version the caller last read.</param>
    /// <param name="strategy">Optional resolution strategy override. Uses the default if not specified.</param>
    /// <returns>A resolution describing the outcome.</returns>
    public ConflictResolution ResolveConflict(
        MemoryFactRecord incomingRecord,
        MemoryFactRecord? currentRecord,
        int expectedVersion,
        ConflictResolutionStrategy? strategy = null)
    {
        ArgumentNullException.ThrowIfNull(incomingRecord);

        var resolvedStrategy = strategy ?? defaultStrategy;

        if (currentRecord is null)
        {
            incomingRecord.Version = 1;
            incomingRecord.PreviousVersionId = null;
            return new ConflictResolution(
                incomingRecord,
                resolvedStrategy);
        }

        var conflictDetected = DetectConflict(expectedVersion, currentRecord);

        if (!conflictDetected)
        {
            incomingRecord.Version = currentRecord.Version + 1;
            incomingRecord.PreviousVersionId = currentRecord.Id;
            return new ConflictResolution(
                incomingRecord,
                resolvedStrategy);
        }

        return resolvedStrategy switch
        {
            ConflictResolutionStrategy.LastWriteWins => ResolveByLastWriteWins(incomingRecord, currentRecord),
            ConflictResolutionStrategy.Merge => ResolveByMerge(incomingRecord, currentRecord),
            ConflictResolutionStrategy.Manual => ResolveByManual(incomingRecord, currentRecord),
            _ => ResolveByLastWriteWins(incomingRecord, currentRecord),
        };
    }

    /// <summary>
    /// Creates the next version of a record from the incoming data, ignoring conflicts.
    /// </summary>
    public ConflictResolution CreateNextVersion(
        MemoryFactRecord incomingRecord,
        MemoryFactRecord currentRecord)
    {
        incomingRecord.Version = currentRecord.Version + 1;
        incomingRecord.PreviousVersionId = currentRecord.Id;
        return new ConflictResolution(
            incomingRecord,
            defaultStrategy);
    }

    static ConflictResolution ResolveByLastWriteWins(
        MemoryFactRecord incomingRecord,
        MemoryFactRecord currentRecord)
    {
        incomingRecord.Version = currentRecord.Version + 1;
        incomingRecord.PreviousVersionId = currentRecord.Id;
        return new ConflictResolution(
            incomingRecord,
            ConflictResolutionStrategy.LastWriteWins,
            PreviousContent: currentRecord.Content,
            PreviousVersion: currentRecord.Version,
            ConflictDetected: true);
    }

    static ConflictResolution ResolveByMerge(
        MemoryFactRecord incomingRecord,
        MemoryFactRecord currentRecord)
    {
        incomingRecord.Version = currentRecord.Version + 1;
        incomingRecord.PreviousVersionId = currentRecord.Id;

        if (!string.IsNullOrWhiteSpace(currentRecord.Content) &&
            !string.Equals(incomingRecord.Content, currentRecord.Content, StringComparison.Ordinal))
        {
            incomingRecord.Content = $"{currentRecord.Content}; {incomingRecord.Content}";
        }

        return new ConflictResolution(
            incomingRecord,
            ConflictResolutionStrategy.Merge,
            PreviousContent: currentRecord.Content,
            PreviousVersion: currentRecord.Version,
            ConflictDetected: true);
    }

    static ConflictResolution ResolveByManual(
        MemoryFactRecord incomingRecord,
        MemoryFactRecord currentRecord)
    {
        incomingRecord.Version = currentRecord.Version + 1;
        incomingRecord.PreviousVersionId = currentRecord.Id;

        return new ConflictResolution(
            incomingRecord,
            ConflictResolutionStrategy.Manual,
            PreviousContent: currentRecord.Content,
            PreviousVersion: currentRecord.Version,
            ConflictDetected: true);
    }
}
