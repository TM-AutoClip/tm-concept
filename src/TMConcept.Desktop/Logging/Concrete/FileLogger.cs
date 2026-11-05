using System.Globalization;
using Microsoft.Extensions.Logging;
using TMConcept.Core.Security;

namespace TMConcept.Desktop.Logging;

/// <summary>One category of <see cref="FileLoggerProvider"/>.</summary>
internal sealed class FileLogger(string category, FileLoggerProvider provider) : ILogger
{
    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
        {
            return;
        }

        ArgumentNullException.ThrowIfNull(formatter);
        var text = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz", CultureInfo.InvariantCulture) + " [" + logLevel + "] " + category + ": "
            + formatter(state, exception);
        if (exception is not null)
        {
            text += Environment.NewLine + exception;
        }

        provider.Write(provider.Scrub(Redactor.Redact(text)));
    }
}
