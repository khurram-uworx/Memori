namespace Memori.Mcp;

/// <summary>
/// Defines how Memori stores and preserves state: either in-memory and transient (Ephemeral) or persisted to durable
/// storage (Durable).
/// </summary>
/// <remarks>Use Ephemeral for transient, short-lived data that does not need to survive process restarts. Use
/// Durable when state must be persisted and recovered across restarts or failures.</remarks>
public enum MemoriMode
{
    Ephemeral,
    Durable
}

/// <summary>
/// Configuration options for Memori MCP that control storage mode, storage path, scope, default entity identifier, and
/// full-text indexing.
/// </summary>
/// <remarks>Use StoragePath and Scope when Mode requires persistent storage. Default values: Mode =
/// MemoriMode.Ephemeral, DefaultEntityId = "default", EnableFullText = false.</remarks>
public class MemoriMcpOptions
{
    public MemoriMode Mode { get; set; } = MemoriMode.Ephemeral;
    public string? StoragePath { get; set; }
    public string? Scope { get; set; }
    public string DefaultEntityId { get; set; } = "default";
    public bool EnableFullText { get; set; } = false;
}
