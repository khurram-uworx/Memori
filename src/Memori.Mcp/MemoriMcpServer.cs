using Memori.Mcp.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;

namespace Memori.Mcp;

sealed class MemoriMcpServer
{
    readonly MemoriMcpOptions options;
    readonly ILogger<MemoriMcpServer> logger;
    readonly IMemoryStore store;

    public MemoriMcpServer(
        IOptions<MemoriMcpOptions> options,
        ILogger<MemoriMcpServer> logger,
        IMemoryStore store)
    {
        this.options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        this.store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        var mcpAssembly = typeof(MemoriMcpServer).Assembly;

        var services = new ServiceCollection();

        services.AddMcpServer()
            .WithStdioServerTransport()
            .WithToolsFromAssembly(mcpAssembly)
            .WithResourcesFromAssembly(mcpAssembly)
            .WithPromptsFromAssembly(mcpAssembly);

        services.AddSingleton(store);
        services.AddSingleton<IOptions<MemoriMcpOptions>>(Options.Create(options));

        var serviceProvider = services.BuildServiceProvider();
        var server = serviceProvider.GetRequiredService<McpServer>();

        logger.LogInformation("Memori MCP server starting");
        await server.RunAsync(cancellationToken);
    }
}
