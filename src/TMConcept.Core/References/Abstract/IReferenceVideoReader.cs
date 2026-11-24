namespace TMConcept.Core.References;

/// <summary>
/// The ffmpeg side of the reference measure (S1, 0.1.52): the grey frames and the small colour frames of the first
/// <see cref="ReferenceLimits.MaxSeconds"/> of a video, read one frame at a time through a temporary file in the work folder that is deleted
/// afterwards. Processor only, at below-normal priority like every child process. The sound is read by <see cref="Music.IMusicAudio"/>.
/// </summary>
public interface IReferenceVideoReader
{
    /// <summary>The grey frame numbers (<see cref="ReferenceFrameReader"/>) at <see cref="ReferenceLimits.FrameFps"/>; null when ffmpeg could not read the video.</summary>
    Task<ReferenceFrameSeries?> ReadFramesAsync(string video, string work, CancellationToken cancellationToken);

    /// <summary>The colour (<see cref="ReferenceColorReader"/>) at <see cref="ReferenceLimits.ColorFps"/>; null when ffmpeg could not read it.</summary>
    Task<ReferenceColors?> ReadColorsAsync(string video, string work, CancellationToken cancellationToken);
}
