using Avalonia.Controls;

namespace TMConcept.Desktop.Startup;

/// <summary>The window dialogs belong to (the main window, or the first-run / unlock window before it).</summary>
public sealed class WindowHost
{
    /// <summary>The active top-level window.</summary>
    public Window? Active { get; set; }
}
