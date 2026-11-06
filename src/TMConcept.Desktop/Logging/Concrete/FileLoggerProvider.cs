using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace TMConcept.Desktop.Logging;

/// <summary>
/// Daily log files in the data folder's <c>logs</c> folder, one per process kind (<see cref="DailyLogFile"/>): Information and above (no
/// Debug), kept 7 days. Every entry goes through <see cref="LogScrubber"/> before it is written: secrets never reach the file even when a
/// provider or a tool echoes them, the user's home folder is written as <c>~</c> and e-mail addresses are masked.
/// </summary>
public sealed class FileLoggerProvider : ILoggerProvider
{
    /// <summary>The lowest level written.</summary>
    public const LogLevel MinimumLevel = LogLevel.Information;

    private readonly ConcurrentDictionary<string, FileLogger> _loggers = new(StringComparer.Ordinal);
    private readonly DailyLogFile _file;
    private readonly LogScrubber _scrubber;
    private readonly TimeProvider _clock;

    /// <summary>Writes the file of <paramref name="kind"/> (<see cref="LogRules.Kinds"/>) in <paramref name="directory"/>.</summary>
    public FileLoggerProvider(string directory, string kind, TimeProvider clock, LogScrubber? scrubber = null, long maxBytes = LogRules.MaxFileBytes)
    {
        ArgumentNullException.ThrowIfNull(clock);
        _clock = clock;
        _scrubber = scrubber ?? LogScrubber.ForCurrentUser;
        _file = new DailyLogFile(directory, kind, clock, maxBytes);
    }

    /// <summary>The file written now.</summary>
    public string CurrentFile => _file.CurrentFile;

    /// <inheritdoc />
    public ILogger CreateLogger(string categoryName) => _loggers.GetOrAdd(categoryName, name => new FileLogger(name, this));

    /// <inheritdoc />
    public void Dispose()
    {
        _loggers.Clear();
        _file.Dispose();
    }

    /// <summary>Formats, scrubs and appends one entry.</summary>
    internal void Write(LogLevel level, string category, EventId eventId, string message, Exception? exception) =>
        _file.Write(_scrubber.Scrub(LogLine.Format(_clock.GetLocalNow(), level, category, eventId, message, exception)));
}
