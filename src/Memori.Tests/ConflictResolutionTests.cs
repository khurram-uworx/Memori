using Memori.Models;
using Memori.Versioning;
using NUnit.Framework;

namespace Memori.Tests;

public sealed class ConflictResolutionTests
{
    static MemoryFactRecord CreateRecord(string id, string content, int version = 1, string? previousVersionId = null)
        => new()
        {
            Id = id,
            EntityId = "entity-1",
            Content = content,
            Embedding = new ReadOnlyMemory<float>(new float[1536]),
            MemoryType = "general",
            Version = version,
            PreviousVersionId = previousVersionId,
        };

    sealed class Fixture
    {
        public VersioningService Service { get; } = new();
    }

    #region Conflict Detection

    [Test]
    public void DetectConflict_NoPreviousRecord_NoConflict()
    {
        var service = new VersioningService();
        var conflict = service.DetectConflict(expectedVersion: 1, currentRecord: null);
        Assert.That(conflict, Is.False);
    }

    [Test]
    public void DetectConflict_MatchingVersion_NoConflict()
    {
        var service = new VersioningService();
        var current = CreateRecord("fact-1", "hello", version: 1);

        var conflict = service.DetectConflict(expectedVersion: 1, current);
        Assert.That(conflict, Is.False);
    }

    [Test]
    public void DetectConflict_MismatchedVersion_ConflictDetected()
    {
        var service = new VersioningService();
        var current = CreateRecord("fact-1", "hello", version: 2);

        var conflict = service.DetectConflict(expectedVersion: 1, current);
        Assert.That(conflict, Is.True);
    }

    #endregion

    #region LastWriteWins

    [Test]
    public void Resolve_LastWriteWins_WithNoConflict_UsesNextVersion()
    {
        var service = new VersioningService();
        var incoming = CreateRecord("fact-1", "updated");
        var current = CreateRecord("fact-1", "original", version: 1);

        var resolution = service.ResolveConflict(incoming, current, expectedVersion: 1,
            ConflictResolutionStrategy.LastWriteWins);

        Assert.That(resolution.ResolvedRecord.Version, Is.EqualTo(2));
        Assert.That(resolution.ResolvedRecord.PreviousVersionId, Is.EqualTo("fact-1"));
        Assert.That(resolution.ConflictDetected, Is.False);
    }

    [Test]
    public void Resolve_LastWriteWins_WithConflict_UsesLastWrite()
    {
        var service = new VersioningService();
        var incoming = CreateRecord("fact-1", "updated content");
        var current = CreateRecord("fact-1", "original content", version: 5);

        var resolution = service.ResolveConflict(incoming, current, expectedVersion: 3,
            ConflictResolutionStrategy.LastWriteWins);

        Assert.That(resolution.ResolvedRecord.Version, Is.EqualTo(6));
        Assert.That(resolution.ResolvedRecord.Content, Is.EqualTo("updated content"));
        Assert.That(resolution.ConflictDetected, Is.True);
        Assert.That(resolution.PreviousContent, Is.EqualTo("original content"));
    }

    [Test]
    public void Resolve_LastWriteWins_NewRecord_SetsVersionToOne()
    {
        var service = new VersioningService();
        var incoming = CreateRecord("fact-1", "new fact");

        var resolution = service.ResolveConflict(incoming, currentRecord: null, expectedVersion: 1,
            ConflictResolutionStrategy.LastWriteWins);

        Assert.That(resolution.ResolvedRecord.Version, Is.EqualTo(1));
        Assert.That(resolution.ResolvedRecord.PreviousVersionId, Is.Null);
        Assert.That(resolution.ConflictDetected, Is.False);
    }

    #endregion

    #region Merge

    [Test]
    public void Resolve_Merge_WithConflict_CombinesContent()
    {
        var service = new VersioningService();
        var incoming = CreateRecord("fact-1", "new info");
        var current = CreateRecord("fact-1", "existing info", version: 3);

        var resolution = service.ResolveConflict(incoming, current, expectedVersion: 2,
            ConflictResolutionStrategy.Merge);

        Assert.That(resolution.ResolvedRecord.Version, Is.EqualTo(4));
        Assert.That(resolution.ResolvedRecord.Content, Does.Contain("existing info"));
        Assert.That(resolution.ResolvedRecord.Content, Does.Contain("new info"));
        Assert.That(resolution.ConflictDetected, Is.True);
    }

    [Test]
    public void Resolve_Merge_WithSameContent_DoesNotDuplicate()
    {
        var service = new VersioningService();
        var incoming = CreateRecord("fact-1", "same content");
        var current = CreateRecord("fact-1", "same content", version: 2);

        var resolution = service.ResolveConflict(incoming, current, expectedVersion: 1,
            ConflictResolutionStrategy.Merge);

        Assert.That(resolution.ResolvedRecord.Content, Is.EqualTo("same content"));
    }

    [Test]
    public void Resolve_Merge_NewRecord_CreatesWithVersionOne()
    {
        var service = new VersioningService();
        var incoming = CreateRecord("fact-1", "new");

        var resolution = service.ResolveConflict(incoming, currentRecord: null, expectedVersion: 1,
            ConflictResolutionStrategy.Merge);

        Assert.That(resolution.ResolvedRecord.Version, Is.EqualTo(1));
        Assert.That(resolution.ResolvedRecord.Content, Is.EqualTo("new"));
    }

    #endregion

    #region Manual

    [Test]
    public void Resolve_Manual_WithConflict_FlagsConflict()
    {
        var service = new VersioningService();
        var incoming = CreateRecord("fact-1", "new content");
        var current = CreateRecord("fact-1", "old content", version: 2);

        var resolution = service.ResolveConflict(incoming, current, expectedVersion: 1,
            ConflictResolutionStrategy.Manual);

        Assert.That(resolution.ConflictDetected, Is.True);
        Assert.That(resolution.StrategyUsed, Is.EqualTo(ConflictResolutionStrategy.Manual));
        Assert.That(resolution.ResolvedRecord.Version, Is.EqualTo(3));
        Assert.That(resolution.PreviousContent, Is.EqualTo("old content"));
    }

    #endregion

    #region CreateNextVersion

    [Test]
    public void CreateNextVersion_IncrementsVersion()
    {
        var service = new VersioningService();
        var incoming = CreateRecord("fact-1", "v2 content");
        var current = CreateRecord("fact-1", "v1 content", version: 1);

        var resolution = service.CreateNextVersion(incoming, current);

        Assert.That(resolution.ResolvedRecord.Version, Is.EqualTo(2));
        Assert.That(resolution.ResolvedRecord.PreviousVersionId, Is.EqualTo("fact-1"));
    }

    #endregion

    #region Default Strategy

    [Test]
    public void Resolve_WithDefaultStrategy_UsesLastWriteWins()
    {
        var service = new VersioningService();
        var incoming = CreateRecord("fact-1", "incoming");
        var current = CreateRecord("fact-1", "current", version: 2);

        var resolution = service.ResolveConflict(incoming, current, expectedVersion: 1);

        Assert.That(resolution.StrategyUsed, Is.EqualTo(ConflictResolutionStrategy.LastWriteWins));
        Assert.That(resolution.ConflictDetected, Is.True);
    }

    [Test]
    public void Constructor_WithCustomDefault_UsesCustomStrategy()
    {
        var service = new VersioningService(ConflictResolutionStrategy.Merge);
        var incoming = CreateRecord("fact-1", "incoming");
        var current = CreateRecord("fact-1", "current", version: 2);

        var resolution = service.ResolveConflict(incoming, current, expectedVersion: 1);

        Assert.That(resolution.StrategyUsed, Is.EqualTo(ConflictResolutionStrategy.Merge));
    }

    #endregion

    #region Edge Cases

    [Test]
    public void Resolve_WithNullIncoming_Throws()
    {
        var service = new VersioningService();
        var current = CreateRecord("fact-1", "content", version: 1);

        Assert.Throws<ArgumentNullException>(() =>
            service.ResolveConflict(null!, current, expectedVersion: 1));
    }

    [Test]
    public void Resolve_WithHighVersionNumbers_WorksCorrectly()
    {
        var service = new VersioningService();
        var incoming = CreateRecord("fact-1", "latest");
        var current = CreateRecord("fact-1", "old", version: 100);

        var resolution = service.ResolveConflict(incoming, current, expectedVersion: 100,
            ConflictResolutionStrategy.LastWriteWins);

        Assert.That(resolution.ResolvedRecord.Version, Is.EqualTo(101));
    }

    [Test]
    public void Resolve_AfterHardDelete_CreatesFreshRecord()
    {
        var service = new VersioningService();
        var incoming = CreateRecord("fact-1", "new after delete");

        var resolution = service.ResolveConflict(incoming, currentRecord: null, expectedVersion: 1);

        Assert.That(resolution.ResolvedRecord.Version, Is.EqualTo(1));
        Assert.That(resolution.ResolvedRecord.PreviousVersionId, Is.Null);
    }

    [Test]
    public void DetectConflict_WithHigherExpectedVersion_DetectsConflict()
    {
        var service = new VersioningService();
        var current = CreateRecord("fact-1", "content", version: 1);

        var conflict = service.DetectConflict(expectedVersion: 3, current);

        Assert.That(conflict, Is.True);
    }

    #endregion
}
