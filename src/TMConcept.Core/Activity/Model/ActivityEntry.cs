namespace TMConcept.Core.Activity;

/// <summary>One line of what the app did, as the screen shows it (the strip at the bottom of the main window): when, how serious, the text.</summary>
public sealed record ActivityEntry(DateTimeOffset At, ActivityLevel Level, string Text)
{
    /// <summary>Whether it is a warning (yellow).</summary>
    public bool IsWarning => Level == ActivityLevel.Warning;

    /// <summary>Whether it is an error (red).</summary>
    public bool IsError => Level == ActivityLevel.Error;
}
