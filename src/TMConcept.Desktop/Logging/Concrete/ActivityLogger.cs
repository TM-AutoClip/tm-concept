using Microsoft.Extensions.Logging;

namespace TMConcept.Desktop.Logging;

/// <summary>One category of <see cref="ActivityLoggerProvider"/>: forwards warnings and errors to the activity panel.</summary>
internal sealed class ActivityLogger(string category, ActivityLoggerProvider provider) : ILogger
{
    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => ActivityLoggerProvider.Forwards(category, logLevel);

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
        {
            return;
        }

        ArgumentNullException.ThrowIfNull(formatter);
        provider.Forward(logLevel, category, formatter(state, exception), exception);
    }
}
