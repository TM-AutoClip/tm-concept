using System.Collections.ObjectModel;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using TMConcept.Core.Activity;
using TMConcept.Core.Security;

namespace TMConcept.Desktop.Logging;

/// <summary>
/// <see cref="IActivityLog"/> for the bottom panel: the last 500 lines (oldest dropped), each also written to the log file (a line that
/// names a profile or a title is written with placeholders instead of the names). Lines are added on the interface thread.
/// </summary>
public sealed class ActivityLog(TimeProvider clock, ILogger<ActivityLog> logger) : IActivityLog
{
    private const int Keep = 500;

    /// <summary>Lines, oldest first.</summary>
    public ObservableCollection<ActivityEntry> Entries { get; } = [];

    /// <inheritdoc />
    public void Add(ActivityLevel level, string text) => Add(level, text, text);

    /// <inheritdoc />
    public void Add(ActivityLevel level, string text, string fileText)
    {
        var entry = new ActivityEntry(clock.GetLocalNow(), level, Redactor.Redact(text));
        var line = ReferenceEquals(text, fileText) ? entry.Text : Redactor.Redact(fileText);
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

        if (Dispatcher.UIThread.CheckAccess())
        {
            Append(entry);
        }
        else
        {
            Dispatcher.UIThread.Post(() => Append(entry));
        }
    }

    /// <summary>Removes every line.</summary>
    public void Clear() => Entries.Clear();

    private void Append(ActivityEntry entry)
    {
        Entries.Add(entry);
        while (Entries.Count > Keep)
        {
            Entries.RemoveAt(0);
        }
    }
}
