namespace TMConcept.Core.Activity;

/// <summary>
/// What stands in the log file for a name the screen shows (<see cref="IActivityLog.Add(ActivityLevel, string, string)"/>): profile names
/// are often channel or account names and titles are the user's content, so they stay on the screen and out of the log file.
/// </summary>
public static class ActivityPlaceholders
{
    /// <summary>A profile's name.</summary>
    public const string Name = "<name>";

    /// <summary>A concept's or a video's title.</summary>
    public const string Title = "<title>";
}
