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

        var entry = new ActivityEntry(clock.GetLocalNow(), level, ReferenceEquals(text, fileText) ? line : Redactor.Redact(text));
        lock (_gate)
        {
            _lines.Enqueue(entry);
            while (_lines.Count > Keep)
            {
                _lines.Dequeue();
            }
        }

        Added?.Invoke(this, entry);
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
