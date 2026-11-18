namespace TMConcept.Core.Media;

/// <summary>A part of a picture or video frame, as shares of its width and height (0–1), for <see cref="IMediaTools.SampleAsync"/>.</summary>
/// <param name="X">Left edge.</param>
/// <param name="Y">Top edge.</param>
/// <param name="Width">Width.</param>
/// <param name="Height">Height.</param>
public sealed record FrameRegion(double X, double Y, double Width, double Height)
{
    /// <summary>The whole picture.</summary>
    public static FrameRegion Whole { get; } = new(0, 0, 1, 1);
}
