using Memori.Mcp.Models;

namespace Memori.Mcp.Storage;

public interface IMemoryStore
{
    Task InsertAsync(McpFactRecord record);
    Task<McpFactRecord?> GetAsync(string id);
    IAsyncEnumerable<McpFactRecord> ListAsync(string entityId, int skip, int limit, bool includeDeleted);
    IAsyncEnumerable<(McpFactRecord Record, double Score)> SearchAsync(string query, string entityId, int limit);
    Task ReplaceAsync(McpFactRecord record);
    Task<bool> DeleteAsync(string id, string entityId);
    Task<int> ClearAsync(string entityId);
}
