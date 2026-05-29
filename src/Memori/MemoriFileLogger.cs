using Microsoft.Extensions.Logging;
using System.Threading.Channels;

namespace Memori;

/// <summary>
/// Provides ILogger instances that write log entries to files in a repository-rooted folder.
/// </summary>
/// <remarks>Creates MemoriFileLogger instances that share a single MemoriFileLogWriter. Dispose the provider to
/// flush and release the underlying writer and related resources.</remarks>
public sealed class MemoriFileLoggerProvider : ILoggerProvider
{
    readonly MemoriFileLogger.MemoriFileLogWriter writer;

    public MemoriFileLoggerProvider(string repoRoot, string logFolder, string version)
        => writer = new MemoriFileLogger.MemoriFileLogWriter(repoRoot, logFolder, version);

    public ILogger CreateLogger(string categoryName)
        => new MemoriFileLogger(categoryName, writer);

    public void Dispose()
        => writer.Dispose();
}

/// <summary>
/// Logs entries to per-process, per-level text files in a repository log folder using an asynchronous background writer
/// and a channel-based queue.
/// </summary>
/// <remarks>Uses an unbounded Channel for concurrent producers and a single background reader that writes one
/// file per LogLevel named Log.{processId}.{level}.txt. Entries are formatted with an ISO 8601 timestamp, log level,
/// category, optional EventId, message, and exception. The writer deletes files older than 30 days on startup and
/// performs non-blocking enqueues via TryWrite. Dispose completes the channel and waits briefly for the background
/// writer to finish. BeginScope returns a no-op scope and IsEnabled returns true for all levels except None.</remarks>
public sealed class MemoriFileLogger : ILogger
{
    sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();

        public void Dispose()
        { }
    }

    internal sealed record LogEntry(
        LogLevel Level,
        string Category,
        EventId EventId,
        DateTimeOffset Timestamp,
        string Message,
        Exception? Exception);

    internal sealed class MemoriFileLogWriter : IDisposable
    {
        static string format(LogEntry entry)
        {
            var text = $"{entry.Timestamp:O} [{entry.Level}] {entry.Category}";

            if (entry.EventId.Id != 0 || !string.IsNullOrEmpty(entry.EventId.Name))
                text += $" ({entry.EventId.Id}:{entry.EventId.Name})";

            text += $": {entry.Message}";

            if (entry.Exception is not null)
                text += Environment.NewLine + entry.Exception;

            return text;
        }

        readonly string logDirectory;
        readonly string logId;
        readonly Channel<LogEntry> channel;
        readonly Task writerTask;
        readonly object disposeLock = new();
        bool disposed;

        internal MemoriFileLogWriter(string repoRoot, string logFolder, string version)
        {
            logDirectory = Path.Combine(repoRoot, logFolder);
            Directory.CreateDirectory(logDirectory);
            deleteOldLogFiles();
            logId = Environment.ProcessId.ToString();

            channel = Channel.CreateUnbounded<LogEntry>(
                new UnboundedChannelOptions
                {
                    SingleReader = true,
                    SingleWriter = false
                });

            writerTask = Task.Run(processQueueAsync);

            tryWrite(new LogEntry(LogLevel.Information, "CodeMemory.Mcp", default, DateTimeOffset.Now, $"MCP server v{version} started", null));
        }

        void deleteOldLogFiles()
        {
            try
            {
                var dir = new DirectoryInfo(logDirectory);
                if (!dir.Exists) return;

                var cutoff = DateTime.UtcNow.AddDays(-30);
                foreach (var file in dir.EnumerateFiles("Log.*.txt"))
                {
                    if (file.LastWriteTimeUtc < cutoff)
                        file.Delete();
                }
            }
            catch
            {
            }
        }

        async Task processQueueAsync()
        {
            var writers = new Dictionary<LogLevel, StreamWriter>();

            try
            {
                await foreach (var entry in channel.Reader.ReadAllAsync())
                {
                    var writer = getWriter(writers, entry.Level);
                    await writer.WriteLineAsync(format(entry));
                    await writer.FlushAsync();
                }
            }
            finally
            {
                foreach (var writer in writers.Values)
                    await writer.DisposeAsync();
            }
        }

        StreamWriter getWriter(Dictionary<LogLevel, StreamWriter> writers, LogLevel level)
        {
            if (writers.TryGetValue(level, out var writer))
                return writer;

            var path = Path.Combine(logDirectory, $"Log.{logId}.{level}.txt");
            writer = new StreamWriter(new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete))
            {
                AutoFlush = false
            };
            writers[level] = writer;
            return writer;
        }

        public void tryWrite(LogEntry entry)
            => channel.Writer.TryWrite(entry);

        public void Dispose()
        {
            lock (disposeLock)
            {
                if (disposed)
                    return;

                disposed = true;
                channel.Writer.TryComplete();
            }

            writerTask.Wait(TimeSpan.FromSeconds(5));
        }
    }

    readonly string categoryName;
    readonly MemoriFileLogWriter writer;

    internal MemoriFileLogger(string categoryName, MemoriFileLogWriter writer)
        => (this.categoryName, this.writer) = (categoryName, writer);

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull
        => NullScope.Instance;

    public bool IsEnabled(LogLevel logLevel)
        => logLevel != LogLevel.None;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
            return;

        var message = formatter(state, exception);
        if (string.IsNullOrWhiteSpace(message) && exception is null)
            return;

        var entry = new LogEntry(
            logLevel,
            categoryName,
            eventId,
            DateTimeOffset.Now,
            message,
            exception);

        writer.tryWrite(entry);
    }
}
