namespace TMConcept.Core.Activity;

/// <summary>
/// The lines of <see cref="IActivityLog"/> for the screen: the newest ones kept in memory (with the names, as the caller wrote them) and an
/// event for every new one. The strip at the bottom of the main window shows the last line and, opened, the last lines.
/// </summary>
public interface IActivityFeed
{
    /// <summary>A line was added (raised on the thread that added it).</summary>
    event EventHandler<ActivityEntry>? Added;

    /// <summary>The kept lines, oldest first.</summary>
    IReadOnlyList<ActivityEntry> Recent();

    /// <summary>
    /// Shows a line that comes from the log (a warning or an error some code logged): it is kept and raised like any line, but not written to
    /// the log file again (it is there already). Skipped when the same text, or a line that contains it, was shown a moment ago (code often
    /// logs a problem and adds it as an activity line too).
    /// </summary>
    void Show(ActivityLevel level, string text);
}
