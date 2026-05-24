using Memori.Mcp.Models;
using Memori.Mcp.Tools;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.VectorData;
using ModelContextProtocol.Server;

namespace Memori.Mcp;

sealed class MemoriMcpServer
{
    readonly MemoriMcpOptions options;
    readonly ILogger<MemoriMcpServer> logger;
    readonly VectorStoreCollection<string, McpFactRecord> factCollection;
    readonly IEmbeddingGenerator<string, Embedding<float>>? embeddingGenerator;

    public MemoriMcpServer(
        IOptions<MemoriMcpOptions> options,
        ILogger<MemoriMcpServer> logger,
        VectorStoreCollection<string, McpFactRecord> factCollection,
        IEmbeddingGenerator<string, Embedding<float>>? embeddingGenerator = null)
    {
        this.options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        this.factCollection = factCollection ?? throw new ArgumentNullException(nameof(factCollection));
        this.embeddingGenerator = embeddingGenerator;
    }

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        var services = new ServiceCollection();

        services.AddMcpServer()
            .WithStdioServerTransport()
            .WithTools<MemoriTools>();

        services.AddSingleton(factCollection);
        services.AddSingleton<IOptions<MemoriMcpOptions>>(Options.Create(options));

        if (embeddingGenerator is not null)
            services.AddSingleton(embeddingGenerator);

        var serviceProvider = services.BuildServiceProvider();
        var server = serviceProvider.GetRequiredService<McpServer>();

        logger.LogInformation("Memori MCP server starting");
        await server.RunAsync(cancellationToken);
    }
}
