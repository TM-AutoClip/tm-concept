using TMConcept.Core.Activity;

namespace TMConcept.Desktop.ViewModels;

/// <summary>An activity line in the activity log panel at the bottom of the main window: the line with its time, and its level for the colour.</summary>
public sealed class ActivityLineViewModel(ActivityEntry entry, string line)
{
    /// <summary>The entry.</summary>
    public ActivityEntry Entry { get; } = entry;

    /// <summary>"14:23 · text".</summary>
    public string Line { get; } = line;

    /// <summary>A warning (yellow).</summary>
    public bool IsWarning => Entry.IsWarning;

    /// <summary>An error (red).</summary>
    public bool IsError => Entry.IsError;
}
