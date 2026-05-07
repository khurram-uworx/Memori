using Microsoft.Extensions.VectorData;

namespace Memori.Models;

/// <summary>
/// Represents a durable fact remembered for an entity, stored as a VectorStore record.
/// </summary>
/// <remarks>
/// This class is annotated with VectorStore attributes to enable storage and retrieval
/// via Microsoft.Extensions.VectorData's VectorStore abstraction.
/// </remarks>
public sealed class MemoryFactRecord
{
    /// <summary>
    /// Creates a new MemoryFactRecord instance.
    /// </summary>
    public MemoryFactRecord()
    {
        // Parameterless constructor required by VectorStore providers
    }

    /// <summary>
    /// Creates a MemoryFactRecord with all fields populated.
    /// </summary>
    /// <param name="id">The unique identifier for this record.</param>
    /// <param name="entityId">The entity this fact belongs to.</param>
    /// <param name="content">The fact content text.</param>
    /// <param name="embedding">The embedding vector for semantic search.</param>
    /// <param name="memoryType">The category of this memory (e.g., "preference", "profile").</param>
    /// <param name="confidence">The confidence score (0.0-1.0).</param>
    /// <param name="createdAt">When this fact was created.</param>
    /// <param name="conversationId">The source conversation ID, if any.</param>
    public MemoryFactRecord(
        string id,
        string entityId,
        string content,
        ReadOnlyMemory<float> embedding,
        string memoryType = "general",
        double confidence = 0.5,
        DateTimeOffset? createdAt = null,
        string? conversationId = null)
    {
        Id = id;
        EntityId = entityId;
        Content = content;
        Embedding = embedding;
        MemoryType = memoryType;
        Confidence = confidence;
        CreatedAt = createdAt ?? DateTimeOffset.UtcNow;
        ConversationId = conversationId;
    }

    /// <summary>
    /// The unique identifier for this record.
    /// </summary>
    [VectorStoreKey]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// The entity this fact belongs to.
    /// </summary>
    [VectorStoreData(IsIndexed = true)]
    public string EntityId { get; set; } = string.Empty;

    /// <summary>
    /// The fact content text.
    /// </summary>
    [VectorStoreData(IsFullTextIndexed = true)]
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// The embedding vector for semantic search.
    /// </summary>
    [VectorStoreVector(1536)]
    public ReadOnlyMemory<float> Embedding { get; set; }

    /// <summary>
    /// The category of this memory (e.g., "preference", "profile").
    /// </summary>
    [VectorStoreData(IsIndexed = true)]
    public string MemoryType { get; set; } = "general";

    /// <summary>
    /// The confidence score (0.0-1.0).
    /// </summary>
    [VectorStoreData(IsIndexed = true)]
    public double Confidence { get; set; } = 0.5;

    /// <summary>
    /// When this fact was created.
    /// </summary>
    [VectorStoreData(IsIndexed = true)]
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// The source conversation ID, if any.
    /// </summary>
    [VectorStoreData]
    public string? ConversationId { get; set; }

    /// <summary>
    /// Summaries associated with this fact.
    /// </summary>
    [VectorStoreData]
    public IReadOnlyList<MemorySummary> Summaries { get; set; } = Array.Empty<MemorySummary>();
}
