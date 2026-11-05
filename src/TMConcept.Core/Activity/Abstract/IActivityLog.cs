namespace TMConcept.Core.Activity;

/// <summary>
/// The user-facing activity log (bottom panel): what the app did, with yellow warnings and red errors. Every line also goes to the log
/// file. Texts are already localized and contain no keys, tokens or personal paths.
/// </summary>
public interface IActivityLog
{
    /// <summary>Adds a line.</summary>
    void Add(ActivityLevel level, string text);
}
