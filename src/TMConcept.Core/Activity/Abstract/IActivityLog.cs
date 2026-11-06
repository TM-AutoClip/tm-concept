namespace TMConcept.Core.Activity;

/// <summary>
/// The user-facing activity log (bottom panel): what the app did, with yellow warnings and red errors. Every line also goes to the log
/// file. Texts are already localized and contain no keys, tokens or personal paths.
/// </summary>
public interface IActivityLog
{
    /// <summary>Adds a line; the log file gets the same text.</summary>
    void Add(ActivityLevel level, string text);

    /// <summary>
    /// Adds a line that names a profile or a title: the panel shows <paramref name="text"/>, the log file gets <paramref name="fileText"/>,
    /// the same line with <see cref="ActivityPlaceholders"/> in place of the names.
    /// </summary>
    void Add(ActivityLevel level, string text, string fileText);
}
