namespace Memori.Mcp;

/// <summary>
/// Defines the storage mode for Memori MCP.
/// </summary>
public enum MemoriMode
{
    Sqlite,
    Markdown
}

/// <summary>
/// Configuration options for Memori MCP that control storage mode, storage path, scope, default entity identifier, and
/// full-text indexing.
/// </summary>
/// <remarks>Default values: Mode = MemoriMode.Sqlite, DefaultEntityId = "default", EnableFullText = false.</remarks>
public class MemoriMcpOptions
{
    public MemoriMode Mode { get; set; } = MemoriMode.Sqlite;
    public string? StoragePath { get; set; }
    public string? Scope { get; set; }
    public string DefaultEntityId { get; set; } = "default";
    public bool EnableFullText { get; set; } = false;
    public string Version { get; set; } = "unknown";
}
