using Memori.Embeddings;
using Memori.Mcp.Models;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.VectorData;

namespace Memori.Mcp;

static class DependencyInjection
{
    const string FactCollectionName = "mcp_facts";

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
        });

        if (!options.EnableFullText)
            services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>, NgramEmbeddingGenerator>();

        services.AddSingleton<MemoriMcpServer>();

        services.TryAddSingleton<VectorStoreCollection<string, McpFactRecord>>(sp =>
        {
            var store = sp.GetRequiredService<VectorStore>();
            var collection = store.GetCollection<string, McpFactRecord>(FactCollectionName);
            collection.EnsureCollectionExistsAsync().GetAwaiter().GetResult();
            return collection;
        });

        return services;
    }
}
