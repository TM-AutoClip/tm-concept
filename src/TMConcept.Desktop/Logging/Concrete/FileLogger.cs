using Microsoft.Extensions.Logging;

namespace TMConcept.Desktop.Logging;

/// <summary>One category of <see cref="FileLoggerProvider"/>; writes Information and above.</summary>
internal sealed class FileLogger(string category, FileLoggerProvider provider) : ILogger
{
    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => logLevel >= FileLoggerProvider.MinimumLevel && logLevel != LogLevel.None;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
        {
            return;
        }

        ArgumentNullException.ThrowIfNull(formatter);
        provider.Write(logLevel, category, eventId, formatter(state, exception), exception);
    }
}
