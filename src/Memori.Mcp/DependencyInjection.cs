using Memori.Embeddings;
using Memori.Mcp.Models;
using Memori.Mcp.Storage;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.VectorData;

namespace Memori.Mcp;

static class DependencyInjection
{
    public static IServiceCollection AddMemoriMcp(
        this IServiceCollection services,
        Action<MemoriMcpOptions>? configure = null)
    {
        var options = new MemoriMcpOptions();
        configure?.Invoke(options);
        services.Configure<MemoriMcpOptions>(opt =>
        {
            opt.Mode = options.Mode;
            opt.StoragePath = options.StoragePath;
            opt.Scope = options.Scope;
            opt.DefaultEntityId = options.DefaultEntityId;
            opt.EnableFullText = options.EnableFullText;
            opt.Version = options.Version;
        });

        if (options.Mode == MemoriMode.Sqlite)
        {
            if (!options.EnableFullText)
                services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>, NgramEmbeddingGenerator>();

            services.TryAddSingleton<IMemoryStore>(sp =>
            {
                var store = sp.GetRequiredService<VectorStore>();
                var collection = store.GetCollection<string, McpFactRecord>("mcp_facts");
                collection.EnsureCollectionExistsAsync().GetAwaiter().GetResult();
                var generator = sp.GetService<IEmbeddingGenerator<string, Embedding<float>>>();
                return new SqliteMemoryStore(collection, generator);
            });
        }
        else
        {
            services.TryAddSingleton<IMemoryStore>(sp =>
                new MarkdownMemoryStore(options.StoragePath));
        }

        services.AddSingleton<MemoriMcpServer>();

        return services;
    }
}
