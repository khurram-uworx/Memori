using Memori;
using Memori.Mcp;
using Memori.Mcp.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Reflection;

var version = Assembly.GetExecutingAssembly()
    .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
    ?.InformationalVersion
    ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString()
    ?? "unknown";

if (args is ["--help" or "-h", ..])
{
    Console.WriteLine($"""
        Memori MCP Server v{version}

        Usage:
          --mode, -m <ephemeral|durable>   Operation mode (default: ephemeral)
          --long                           Shorthand for --mode durable
          --path, -p <path>                Storage path (default: .memori/ in durable mode)
          --fulltext                       Enable Full Text search (FTS5, default n-gram vector search)
          --verbose, -v                    Enable debug logging
          --help, -h                       Show this help

        Examples:
          memori-mcp
          memori-mcp --mode durable
          memori-mcp --long --path ./my-memories --verbose
          memori-mcp --vectors
        """);
    return 0;
}

var (mode, storagePath, verbose, fullText) = CliParser.Parse(args);

var builder = Host.CreateApplicationBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);
builder.Logging.AddProvider(new MemoriFileLoggerProvider(Environment.CurrentDirectory, ".memori", version));

builder.Logging.AddConsole();
builder.Logging.SetMinimumLevel(verbose ? LogLevel.Debug : LogLevel.Information);

if (mode == MemoriMode.Durable)
{
    builder.Services.AddMemoriSqliteStorage(options =>
    {
        if (storagePath is not null)
            options.DatabasePath = Path.Combine(storagePath, "memori.db");
    });
}

builder.Services.AddMemoriMcp(options =>
{
    options.Mode = mode;
    options.StoragePath = storagePath;
    options.EnableFullText = fullText;
});

builder.Services.AddHostedService<MemoriMcpHostedService>();

var host = builder.Build();

await host.RunAsync();

return 0;

class MemoriMcpHostedService(MemoriMcpServer server, ILogger<MemoriMcpHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await server.RunAsync(stoppingToken);
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("Memori MCP server stopped");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Memori MCP server failed");
            throw;
        }
    }
}
