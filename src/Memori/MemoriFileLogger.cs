using Microsoft.Extensions.Logging;
using System.Threading.Channels;

namespace Memori;

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

sealed class MemoriFileLogger : ILogger
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
