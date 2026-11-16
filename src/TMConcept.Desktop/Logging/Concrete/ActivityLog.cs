using Microsoft.Extensions.Logging;
using TMConcept.Core.Activity;
using TMConcept.Core.Security;

namespace TMConcept.Desktop.Logging;

/// <summary>
/// <see cref="IActivityLog"/> and <see cref="IActivityFeed"/>: every line goes to the log file, complete, for finding a problem later (a line
/// that names a profile or a title is written with placeholders instead of the names), and the newest <see cref="Keep"/> lines stay in
/// memory with the names for the strip at the bottom of the main window (the developer asked for it on 8 Nov 2026: "what was done must be
/// shown somewhere, at worst at the bottom of the screen"; one line and a list that opens on a click, not the old panel). Lines may come
/// from any thread; <see cref="Added"/> is raised on the adding one.
/// </summary>
public sealed class ActivityLog(TimeProvider clock, ILogger<ActivityLog> logger) : IActivityLog, IActivityFeed
{
    /// <summary>Lines kept in memory.</summary>
    public const int Keep = 200;

    /// <summary>How long a shown line hides the same text coming from the log (<see cref="Show"/>).</summary>
    public static readonly TimeSpan Repeat = TimeSpan.FromSeconds(10);

    // How many of the newest lines are compared with a line from the log.
    private const int RepeatLines = 8;

    private readonly Lock _gate = new();
    private readonly Queue<ActivityEntry> _lines = new();

    /// <inheritdoc />
    public event EventHandler<ActivityEntry>? Added;

    /// <inheritdoc />
    public void Add(ActivityLevel level, string text) => Add(level, text, text);

    /// <inheritdoc />
    public void Add(ActivityLevel level, string text, string fileText)
    {
        var line = Redactor.Redact(fileText);
        switch (level)
        {
            case ActivityLevel.Error:
                logger.LogError("Activity: {Text}", line);
                break;
            case ActivityLevel.Warning:
                logger.LogWarning("Activity: {Text}", line);
                break;
            default:
                logger.LogInformation("Activity: {Text}", line);
                break;
        }

        Store(new ActivityEntry(clock.GetLocalNow(), level, ReferenceEquals(text, fileText) ? line : Redactor.Redact(text)), repeatCheck: false);
    }

    /// <inheritdoc />
    public void Show(ActivityLevel level, string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        Store(new ActivityEntry(clock.GetLocalNow(), level, Redactor.Redact(text)), repeatCheck: true);
    }

    private void Store(ActivityEntry entry, bool repeatCheck)
    {
        lock (_gate)
        {
            if (repeatCheck && _lines.Reverse().Take(RepeatLines).Any(l => entry.At - l.At <= Repeat && Same(l.Text, entry.Text)))
            {
                return;
            }

            _lines.Enqueue(entry);
            while (_lines.Count > Keep)
            {
                _lines.Dequeue();
            }
        }

        Added?.Invoke(this, entry);
    }

    // The same line: equal, or one holds the other (a log line "Source: message" and a code line that quotes the message).
    private static bool Same(string shown, string text)
    {
        var message = text[(text.IndexOf(": ", StringComparison.Ordinal) + 1)..].Trim();
        return string.Equals(shown, text, StringComparison.Ordinal) || shown.Contains(text, StringComparison.Ordinal)
            || (message.Length >= 12 && shown.Contains(message, StringComparison.Ordinal));
    }

    /// <inheritdoc />
    public IReadOnlyList<ActivityEntry> Recent()
    {
        lock (_gate)
        {
            return [.. _lines];
        }
    }
}
