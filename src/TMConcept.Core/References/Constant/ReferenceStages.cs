namespace TMConcept.Core.References;

/// <summary>The stages of a reference measure (<see cref="ReferenceProgress.Stage"/>; the interface names them "reference.stage.&lt;stage&gt;").</summary>
public static class ReferenceStages
{
    /// <summary>yt-dlp downloads the video.</summary>
    public const string Download = "download";

    /// <summary>ffmpeg reads the grey frames (cuts, camera).</summary>
    public const string Frames = "frames";

    /// <summary>ffmpeg reads the colour frames.</summary>
    public const string Colors = "colors";

    /// <summary>ffmpeg reads the sound (loudness, beat grid).</summary>
    public const string Sound = "sound";

    /// <summary>The numbers are put together.</summary>
    public const string Done = "done";
}
