namespace TMConcept.Core.Media;

/// <summary>What the mux draws over the video, and the music it lays under the narration (0.1.44).</summary>
/// <param name="AssFile">The ASS file burned in (a file name inside the mux folder), or <c>null</c>.</param>
/// <param name="FontsFolder">A folder inside the mux folder with the fonts the ASS file names (libass loads them), or <c>null</c>.</param>
/// <param name="Photo">A round picture laid over the video, or <c>null</c>.</param>
/// <param name="Music">Background music under the narration, or <c>null</c> (the video sounds as before 0.1.44).</param>
public sealed record MuxOverlay(string? AssFile, string? FontsFolder = null, MuxPhoto? Photo = null, MuxMusic? Music = null)
{
    /// <summary>Nothing drawn over the video.</summary>
    public static MuxOverlay None { get; } = new(AssFile: null);
}
