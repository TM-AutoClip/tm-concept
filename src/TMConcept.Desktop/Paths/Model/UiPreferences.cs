namespace TMConcept.Desktop.Paths;

/// <summary>Plain preferences needed before unlocking or before the window exists (language, drawing). Nothing secret.</summary>
public sealed class UiPreferences
{
    /// <summary>Interface language (<c>en</c> / <c>tr</c>); empty until chosen.</summary>
    public string Language { get; set; } = string.Empty;

    /// <summary>
    /// Draw the window with the graphics card (Direct3D / OpenGL). On by default since 0.1.7 (the developer's choice: scrolling and the
    /// player are smoother); 0.1.3–0.1.6 drew on the processor by default, which keeps about 50–70 MB less memory (the graphics driver's own
    /// memory and threads live in the app's process). A preferences file that says false keeps it off. Read at start.
    /// </summary>
    public bool UseGraphicsCard { get; set; } = true;

    /// <summary>
    /// Whether the activity log at the bottom of the main window is open. Folded by default since 0.1.13 (one line with the newest entry and
    /// the running step; open it was a quarter of a 720-pixel window); the last choice is kept.
    /// </summary>
    public bool LogOpen { get; set; }
}
