using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using TMConcept.Core.Activity;

namespace TMConcept.Desktop.Logging;

/// <summary>
/// Warnings and errors of every <see cref="ILogger"/> also reach the activity panel at the bottom of the main window (the developer, 16 Nov
/// 2026: "it is open source, people must see what happens"; 0.1.15 wrote sd-cli's failure only to the log file). A line is shown through
/// <see cref="IActivityFeed.Show"/>, which does not log it again; the activity log's own category is never forwarded (its lines are the
/// panel's lines already: that would loop). The text is one line (the message, then the exception's type and message, no stack), masked by
/// <see cref="LogScrubber"/> (paths, home folder, e-mail addresses, keys) and cut at <see cref="MaxLength"/>. The feed is resolved on the
/// first line, not at construction: the activity log needs a logger, so taking it in the constructor would be a cycle.
/// </summary>
public sealed class ActivityLoggerProvider(Func<IActivityFeed?> feed, LogScrubber scrubber) : ILoggerProvider
{
    /// <summary>The lowest level shown in the panel.</summary>
    public const LogLevel MinimumLevel = LogLevel.Warning;

    /// <summary>Longest text of one forwarded line (the log file keeps all of it).</summary>
    public const int MaxLength = 400;

    /// <summary>The category that is never forwarded: the activity log writes its own lines to the log file under it.</summary>
    public static readonly string SkippedCategory = typeof(ActivityLog).FullName!;

    // A line logged while a line is shown (by a handler of the feed's event) is not forwarded again on that thread.
    [ThreadStatic]
    private static bool t_showing;

    private readonly ConcurrentDictionary<string, ActivityLogger> _loggers = new(StringComparer.Ordinal);
    private readonly Lazy<IActivityFeed?> _feed = new(feed);
    private bool _disposed;

    /// <inheritdoc />
    public ILogger CreateLogger(string categoryName) => _loggers.GetOrAdd(categoryName, name => new ActivityLogger(name, this));

    /// <inheritdoc />
    public void Dispose()
    {
        _disposed = true;
        _loggers.Clear();
    }

    /// <summary>Whether lines of <paramref name="category"/> at <paramref name="level"/> are forwarded.</summary>
    internal static bool Forwards(string category, LogLevel level) =>
        level >= MinimumLevel && level != LogLevel.None && !string.Equals(category, SkippedCategory, StringComparison.Ordinal);

    /// <summary>The panel's text of one entry: one line, masked, short.</summary>
    public string Text(string category, string message, Exception? exception)
    {
        ArgumentNullException.ThrowIfNull(category);
        var source = category[(category.LastIndexOf('.') + 1)..];
        var text = exception is null || message.Contains(exception.Message, StringComparison.Ordinal)
            ? message
            : message + " — " + exception.GetType().Name + ": " + exception.Message;
        text = scrubber.Scrub(source + ": " + text).ReplaceLineEndings(" ").Trim();
        return text.Length <= MaxLength ? text : text[..(MaxLength - 1)] + "…";
    }

    /// <summary>Shows one entry in the panel (never throws: a log line must not break the code that wrote it).</summary>
    internal void Forward(LogLevel level, string category, string message, Exception? exception)
    {
        if (_disposed || t_showing)
        {
            return;
        }

        t_showing = true;
        try
        {
            if (_feed.Value is { } target)
            {
                target.Show(level >= LogLevel.Error ? ActivityLevel.Error : ActivityLevel.Warning, Text(category, message, exception));
            }
        }
        catch (ObjectDisposedException)
        {
            // The app is closing: the container is gone, the log file still has the line.
            _disposed = true;
        }
        catch (InvalidOperationException)
        {
            // The feed could not be made (a line logged while the container builds the activity log itself): the log file has it.
        }
        finally
        {
            t_showing = false;
        }
    }
}
