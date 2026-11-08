namespace TMConcept.Core.Media;

/// <summary>Video file work done with ffmpeg (processor only).</summary>
public interface IMediaTools
{
    /// <summary>Components the tools need (ffmpeg); installed, after a confirmation, before the first video when missing.</summary>
    IReadOnlyList<string> RequiredComponents { get; }

    /// <summary>
    /// Joins the silent video with narration clips (at their start), burns <paramref name="assFile"/> (a file in <paramref name="folder"/>) in
    /// and fades through black at each of <paramref name="fadesMs"/> (the starts of the scenes with a soft transition).
    /// </summary>
    Task MuxAsync(string folder, string silentVideo, IReadOnlyList<(string Wav, long StartMs)> clips, string? assFile, IReadOnlyList<long> fadesMs, string output,
        CancellationToken cancellationToken);

    /// <summary>Writes a thumbnail from the first frame that is not black; false when none was found.</summary>
    Task<bool> ThumbnailAsync(string video, string jpeg, CancellationToken cancellationToken);
}
