namespace TMConcept.Core.Activity;

/// <summary>
/// What the app did, for the log file (info, warnings, errors; complete, for finding a problem later). Texts are already localized and
/// contain no keys, tokens or personal paths. There is no activity panel (0.1.7): a line the user must see is also shown on its page or
/// as a notification by the caller.
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
