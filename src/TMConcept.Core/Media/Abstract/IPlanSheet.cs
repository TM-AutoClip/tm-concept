namespace TMConcept.Core.Media;

/// <summary>
/// The ffmpeg side of the plan preview (F10, 0.1.49): the frames of a video at given moments laid out as one sheet (ffmpeg's tile filter).
/// Processor only, at below-normal priority like every child process.
/// </summary>
public interface IPlanSheet
{
    /// <summary>
    /// Writes <paramref name="png"/>: the frame at each of <paramref name="seconds"/> of <paramref name="video"/>, <paramref name="columns"/>
    /// side by side, row after row. False when ffmpeg could not write it (the preview then lists the scenes without the sheet).
    /// </summary>
    Task<bool> SheetAsync(string video, IReadOnlyList<double> seconds, int columns, string png, CancellationToken cancellationToken);
}
