namespace TMConcept.Core.Activity;

/// <summary>One line of the activity log shown at the bottom of the main window.</summary>
public sealed record ActivityEntry(DateTimeOffset At, ActivityLevel Level, string Text)
{
    /// <summary>Whether it is a warning (yellow).</summary>
    public bool IsWarning => Level == ActivityLevel.Warning;

    /// <summary>Whether it is an error (red).</summary>
    public bool IsError => Level == ActivityLevel.Error;

    /// <summary>Glyph by level.</summary>
    public string Glyph => Level switch
    {
        ActivityLevel.Warning => "⚠",
        ActivityLevel.Error => "✖",
        _ => "ℹ",
    };
}
