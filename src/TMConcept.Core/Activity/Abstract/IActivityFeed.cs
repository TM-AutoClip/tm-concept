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
}
