namespace TMConcept.Desktop.Paths;

/// <summary>Plain preferences needed before unlocking or before the window exists (language, drawing). Nothing secret.</summary>
public sealed class UiPreferences
{
    /// <summary>Interface language (<c>en</c> / <c>tr</c>); empty until chosen.</summary>
    public string Language { get; set; } = string.Empty;

    /// <summary>
    /// Draw the window with the graphics card (Direct3D / OpenGL). Off by default since 0.1.3: drawing on the processor looks the same and
    /// keeps about 50–70 MB less memory (the graphics driver's own memory and threads live in the app's process). Read at start.
    /// </summary>
    public bool UseGraphicsCard { get; set; }
}
