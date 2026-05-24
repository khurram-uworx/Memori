namespace Memori.Mcp;

enum MemoriMode
{
    Ephemeral,
    Durable
}

class MemoriMcpOptions
{
    public MemoriMode Mode { get; set; } = MemoriMode.Ephemeral;
    public string? StoragePath { get; set; }
    public string? Scope { get; set; }
    public string DefaultEntityId { get; set; } = "default";
    public bool EnableFullText { get; set; } = false;
}
