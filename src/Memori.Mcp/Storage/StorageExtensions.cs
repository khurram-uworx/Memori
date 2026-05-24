using Memori.Mcp.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.VectorData;

namespace Memori.Mcp.Storage;

/// <summary>
/// Extension methods for registering SQLite-based Memori storage services.
/// </summary>
public static class StorageExtensions
{
    const string FactCollectionName = "mcp_facts";

    /// <summary>
    /// Adds SQLite-backed vector store and fact collection to the service collection.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to add services to.</param>
    /// <param name="configure">Optional delegate to configure <see cref="SqliteStorageOptions"/>.</param>
    /// <returns>The <see cref="IServiceCollection"/> for chaining.</returns>
    public static IServiceCollection AddMemoriSqliteStorage(
        this IServiceCollection services,
        Action<SqliteStorageOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = new SqliteStorageOptions();
        configure?.Invoke(options);

        services.AddSingleton(options);
        services.AddSingleton<VectorStore>(sp =>
            new SqliteVectorStore(options));
        services.AddSingleton(sp =>
        {
            var store = sp.GetRequiredService<VectorStore>();
            var collection = store.GetCollection<string, McpFactRecord>(FactCollectionName);
            collection.EnsureCollectionExistsAsync().GetAwaiter().GetResult();
            return collection;
        });

        return services;
    }
}
