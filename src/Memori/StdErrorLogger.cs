using Microsoft.Extensions.Logging;

namespace Memori;

/// <summary>
/// Provides an ILogger<T> implementation that writes log messages to the standard error stream.
/// </summary>
/// <remarks>Writes log entries to Console.Error. LogLevel.Information entries are written without a level prefix;
/// other levels are prefixed with the level name. The logger is always enabled. BeginScope returns a no-op scope (the
/// logger itself) and Dispose is a no-op.</remarks>
/// <typeparam name="T">The type whose name is used for the logger category.</typeparam>
public class StdErrorLogger<T> : ILogger<T>, IDisposable
{
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (logLevel != LogLevel.Information)
            Console.Error.WriteLine($"{logLevel}: {state}");
        else
            Console.Error.WriteLine(state);
    }

    public bool IsEnabled(LogLevel logLevel) => true;

    public IDisposable BeginScope<TState>(TState state) where TState : notnull => this;

    public void Dispose()
    { }
}
