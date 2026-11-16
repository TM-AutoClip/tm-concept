using Microsoft.Extensions.Logging;
using TMConcept.Core.Activity;

namespace TMConcept.Desktop.Logging;

/// <summary>
/// One category of <see cref="ActivityLoggerProvider"/>: forwards warnings and errors to the activity panel, except the lines marked
/// <see cref="LogEvents.OnScreen"/> (their failure gets its own line in the user's language).
/// </summary>
internal sealed class ActivityLogger(string category, ActivityLoggerProvider provider) : ILogger
{
    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => ActivityLoggerProvider.Forwards(category, logLevel);

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        // A line whose failure the catching code shows on screen itself (LogEvents.OnScreen) stays in the log file only.
        if (!IsEnabled(logLevel) || eventId.Id == LogEvents.OnScreen.Id)
        {
            return;
        }

        ArgumentNullException.ThrowIfNull(formatter);
        provider.Forward(logLevel, category, formatter(state, exception), exception);
    }
}
