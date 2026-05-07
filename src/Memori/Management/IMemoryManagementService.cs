using Memori.Models;

namespace Memori.Management;

/// <summary>
/// Provides user-facing APIs to inspect, search, filter, edit, and delete stored memories.
/// </summary>
/// <remarks>
/// <para>
/// This service enables transparency and user control over durable memory. Applications
/// can expose these operations through settings pages, privacy dashboards, or admin panels.
/// </para>
/// <para>
/// <strong>Access control is the application's responsibility.</strong> This service does
/// not enforce authorization. Callers should verify that the requesting user has permission
/// to view or modify the memories being accessed.
/// </para>
/// </remarks>
public interface IMemoryManagementService
{
    /// <summary>
    /// Lists all memory records for a given entity, with optional pagination.
    /// </summary>
    /// <param name="entityId">The entity whose memories to list.</param>
    /// <param name="skip">Number of records to skip (for pagination).</param>
    /// <param name="take">Maximum number of records to return.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A list of memory fact records.</returns>
    ValueTask<IReadOnlyList<MemoryFactRecord>> ListMemoriesAsync(
        string entityId,
        int skip = 0,
        int take = 50,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Searches memories for an entity by content text, with optional type and scope filters.
    /// </summary>
    /// <param name="entityId">The entity whose memories to search.</param>
    /// <param name="searchText">The text to search for in memory content.</param>
    /// <param name="memoryType">Optional memory type filter.</param>
    /// <param name="scope">Optional workspace scope filter.</param>
    /// <param name="includeDeleted">Whether to include soft-deleted memories.</param>
    /// <param name="take">Maximum number of results.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Matching memory fact records.</returns>
    ValueTask<IReadOnlyList<MemoryFactRecord>> SearchMemoriesAsync(
        string entityId,
        string searchText,
        string? memoryType = null,
        string? scope = null,
        bool includeDeleted = false,
        int take = 50,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a single memory record by its ID.
    /// </summary>
    /// <param name="memoryId">The memory record ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The memory record, or null if not found.</returns>
    ValueTask<MemoryFactRecord?> GetMemoryAsync(
        string memoryId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates the content of a memory record.
    /// </summary>
    /// <param name="memoryId">The memory record ID to update.</param>
    /// <param name="newContent">The new content text.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the record was found and updated; false if not found.</returns>
    ValueTask<bool> UpdateMemoryAsync(
        string memoryId,
        string newContent,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Soft-deletes a memory record. The record remains in storage but is excluded from recall.
    /// </summary>
    /// <param name="memoryId">The memory record ID to soft-delete.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the record was found and soft-deleted; false if not found.</returns>
    ValueTask<bool> SoftDeleteMemoryAsync(
        string memoryId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Permanently deletes a memory record from storage.
    /// </summary>
    /// <param name="memoryId">The memory record ID to permanently delete.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the record was found and deleted; false if not found.</returns>
    ValueTask<bool> HardDeleteMemoryAsync(
        string memoryId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Restores a soft-deleted memory record.
    /// </summary>
    /// <param name="memoryId">The memory record ID to restore.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the record was found and restored; false if not found.</returns>
    ValueTask<bool> RestoreMemoryAsync(
        string memoryId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the total count of memory records for an entity.
    /// </summary>
    /// <param name="entityId">The entity whose memories to count.</param>
    /// <param name="includeDeleted">Whether to include soft-deleted memories.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The total memory count.</returns>
    ValueTask<int> GetMemoryCountAsync(
        string entityId,
        bool includeDeleted = false,
        CancellationToken cancellationToken = default);
}
