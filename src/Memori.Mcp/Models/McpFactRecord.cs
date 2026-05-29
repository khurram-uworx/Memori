using Microsoft.Extensions.VectorData;

namespace Memori.Mcp.Models;

/// <summary>
/// MCP-specific fact record stored in the VectorStore.
/// Independent from the core Memori library's MemoryFactRecord.
/// </summary>
public sealed class McpFactRecord
{
    [VectorStoreKey]
    public string Id { get; set; } = string.Empty;

    [VectorStoreData(IsIndexed = true)]
    public string EntityId { get; set; } = string.Empty;

    [VectorStoreData(IsFullTextIndexed = true)]
    public string Content { get; set; } = string.Empty;

    [VectorStoreVector(1536)]
    public ReadOnlyMemory<float> Embedding { get; set; }

    [VectorStoreData(IsIndexed = true)]
    public string? MemoryType { get; set; }

    [VectorStoreData(IsFullTextIndexed = true)]
    public string? Tags { get; set; }

    [VectorStoreData]
    public double Confidence { get; set; } = 1.0;

    [VectorStoreData(IsIndexed = true)]
    public DateTimeOffset CreatedAt { get; set; }

    [VectorStoreData(IsIndexed = true)]
    public int Version { get; set; } = 1;

    [VectorStoreData(IsIndexed = true)]
    public bool IsDeleted { get; set; }
}
