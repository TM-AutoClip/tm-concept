namespace TMConcept.Core.Videos;

/// <summary>
/// The limits of the frame check after the render (<see cref="FrameCheck"/>). Tuned to be conservative: a false failure throws a good
/// video away (or replaces a local model's design with the app's own), so only an almost empty frame, or one that has nothing to do with
/// its scene image, fails. The scores are logged for every scene so the limits can be tuned from real videos.
/// </summary>
public static class FrameCheckLimits
{
    /// <summary>
    /// A frame whose brightness varies less than this (standard deviation, 0–255) is near-uniform: one colour with at most a thin line
    /// (the 19 Nov 2026 video: a dark red background with a red bar). A small title on a plain background is far above it.
    /// </summary>
    public const double UniformDeviation = 3.0;

    /// <summary>
    /// A frame whose best correlation with its scene image is below this does not show the image (1 = the same picture; an image with a
    /// slow zoom, a pan, a tint or a few shapes over it stays well above 0.5).
    /// </summary>
    public const double MinResemblance = 0.2;

    /// <summary>Columns of the grey grid the frame and the image are compared on (small, so a zoom or a few pixels' shift do not matter).</summary>
    public const int GridWidth = 24;

    /// <summary>Rows of the grey grid.</summary>
    public const int GridHeight = 12;
}
