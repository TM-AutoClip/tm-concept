namespace TMConcept.Core.Media;

/// <summary>
/// The ffmpeg side of the motion check (<see cref="Videos.MotionCheck"/>, 0.1.41): every frame of a video as small grey numbers, and a strip
/// of frames around a moment as a picture. Processor only, at below-normal priority like every child process.
/// </summary>
public interface IMotionFrames
{
    /// <summary>
    /// The numbers of every frame of <paramref name="video"/> (<see cref="Videos.MotionFrameReader"/>) at <paramref name="fps"/>; the raw frames
    /// go through a temporary file in <paramref name="workFolder"/> that is deleted afterwards. Null when ffmpeg could not read the video.
    /// </summary>
    Task<Videos.MotionSeries?> ReadAsync(string video, double fps, string workFolder, CancellationToken cancellationToken);

    /// <summary>
    /// Writes <paramref name="png"/>: <see cref="Videos.MotionCheckLimits.StripFrames"/> frames side by side around <paramref name="atSeconds"/>,
    /// spread over <paramref name="spanSeconds"/> (0 = consecutive frames). False when ffmpeg could not write it.
    /// </summary>
    Task<bool> StripAsync(string video, double atSeconds, double spanSeconds, double fps, string png, CancellationToken cancellationToken);
}
