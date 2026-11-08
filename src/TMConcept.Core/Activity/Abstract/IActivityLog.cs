namespace TMConcept.Core.Activity;

/// <summary>
/// What the app did: every line goes to the log file (info, warnings, errors; complete, for finding a problem later) and to the one-line
/// strip at the bottom of the main window (<see cref="IActivityFeed"/>; the last line, the last lines when opened). Texts are already
/// localized and contain no keys, tokens or personal paths; a line about money says what was paid. A line the user must act on is also
/// shown on its page or as a notification by the caller.
/// </summary>
public interface IActivityLog
{
    /// <summary>Adds a line to the log file.</summary>
    void Add(ActivityLevel level, string text);

    /// <summary>
    /// Adds a line that names a profile or a title: the log file gets <paramref name="fileText"/>, the same line with
    /// <see cref="ActivityPlaceholders"/> in place of the names (<paramref name="text"/> is the line with the names, as a caller shows it).
    /// </summary>
    void Add(ActivityLevel level, string text, string fileText);
}
