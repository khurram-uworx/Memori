namespace Memori.Mcp.Storage;

/// <summary>
/// Configuration options for SQLite-based Memori storage.
/// </summary>
public class SqliteStorageOptions
{
    /// <summary>
    /// Path to the SQLite database file. May be relative or absolute.
    /// Defaults to <c>.memori/memori.db</c>.
    /// </summary>
    public string DatabasePath { get; set; } = ".memori/memori.db";

    /// <summary>
    /// When <c>true</c>, the database directory and file are created automatically
    /// on first access.
    /// </summary>
    public bool AutoCreateDatabase { get; set; } = true;
}
