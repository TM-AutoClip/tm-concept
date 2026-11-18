namespace TMConcept.Core.Media;

/// <summary>Video file work done with ffmpeg (processor only).</summary>
public interface IMediaTools
{
    /// <summary>Components the tools need (ffmpeg); installed, after a confirmation, before the first video when missing.</summary>
    IReadOnlyList<string> RequiredComponents { get; }

    /// <summary>
    /// Joins the silent video with narration clips (at their start), draws <paramref name="overlay"/> over it (an ASS file burned in with the
    /// fonts of its folder, a round photo; every file in <paramref name="folder"/>) and fades through black at each of <paramref name="fadesMs"/>
    /// (the starts of the scenes with a soft transition).
    /// </summary>
    Task MuxAsync(string folder, string silentVideo, IReadOnlyList<(string Wav, long StartMs)> clips, MuxOverlay overlay, IReadOnlyList<long> fadesMs, string output,
        CancellationToken cancellationToken);

    /// <summary>Writes a thumbnail from the first frame that is not black; false when none was found.</summary>
    Task<bool> ThumbnailAsync(string video, string jpeg, CancellationToken cancellationToken);

    /// <summary>
    /// Small RGB copies of <paramref name="region"/> of <paramref name="file"/> (a video, or a picture with one moment 0) at each of
    /// <paramref name="atMs"/>, in order, for <see cref="Styles.TextBackdrop"/>; <c>null</c> for a moment ffmpeg could not read (past the end).
    /// </summary>
    Task<IReadOnlyList<FramePixels?>> SampleAsync(string file, IReadOnlyList<long> atMs, FrameRegion region, CancellationToken cancellationToken);
}
