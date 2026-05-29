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
          --mode, -m <sqlite|markdown>     Operation mode (default: sqlite)
          --markdown                       Shorthand for --mode markdown
          --path, -p <path>                Storage path (default per mode)
          --fulltext                       Enable Full Text search (FTS5, default n-gram vector search)
          --debug                          Enable debug logging
          --version                        Show version
          --help, -h                       Show this help

        Examples:
          memori-mcp
          memori-mcp --mode markdown --path ./project-memories
          memori-mcp --markdown --path C:\Absolute\Path
          memori-mcp --path ./my-memories --debug
        """);
    return 0;
}

if (args is ["--version", ..])
{
    Console.WriteLine($"memori-mcp v{version}");
    return 0;
}

var (mode, storagePath, debug, fullText) = CliParser.Parse(args);

var builder = Host.CreateApplicationBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);
builder.Logging.AddProvider(new MemoriFileLoggerProvider(Environment.CurrentDirectory, ".memori", version));

builder.Logging.AddConsole();
builder.Logging.SetMinimumLevel(debug ? LogLevel.Debug : LogLevel.Information);

if (mode == MemoriMode.Sqlite)
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
    options.Version = version;
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
