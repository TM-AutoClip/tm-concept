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

    /// <summary>
    /// Which stable-diffusion.cpp build runs the local image models on this computer (per machine: the graphics card belongs to the
    /// computer, not to the backup). Automatic by default: chosen from the graphics card found. Stored as a number.
    /// </summary>
    public Core.Hardware.ImageAcceleration ImageAcceleration { get; set; }

    /// <summary>
    /// The app version that last opened the main window (<c>0.1.16</c>), written after the version check (<c>VersionCheck</c>): another
    /// version at start asks what to do with the installed components. Empty = not recorded (0.1.15 and earlier).
    /// </summary>
    public string LastVersion { get; set; } = string.Empty;

    /// <summary>
    /// The "Subtitles in the picture" choice of the Concept to video page (on by default). Kept since 0.1.17: until then it came back on
    /// after every restart, and a queued item kept the choice it was added with, so turning it off did not stop the subtitles.
    /// </summary>
    public bool Subtitles { get; set; } = true;

    /// <summary>Look for a newer version of the app at start (once a day at most; Settings → General). On by default.</summary>
    public bool CheckForUpdates { get; set; } = true;

    /// <summary>The newer version last offered at start (with <see cref="UpdateOfferedAtUtc"/>: not offered again within a day).</summary>
    public string UpdateOfferedVersion { get; set; } = string.Empty;

    /// <summary>When <see cref="UpdateOfferedVersion"/> was offered (UTC).</summary>
    public DateTimeOffset? UpdateOfferedAtUtc { get; set; }
}
