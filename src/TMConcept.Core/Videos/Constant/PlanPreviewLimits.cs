namespace TMConcept.Core.Videos;

/// <summary>
/// The plan preview's numbers (F10, 0.1.49; ours). The preview is the app's drawer rendered on this computer with placeholders instead of the
/// scene images, small and at a low frame rate (the sheet needs single frames, not motion), then the key moment of every beat is cut out
/// and laid out as one sheet by ffmpeg's tile filter.
/// </summary>
public static class PlanPreviewLimits
{
    /// <summary>At most this many still frames on a sheet (more beats: evenly chosen).</summary>
    public const int MaxFrames = 24;

    /// <summary>Frames per second of the preview render.</summary>
    public const int Fps = 5;

    /// <summary>The short side of the preview render, pixels (the long side follows the video's shape).</summary>
    public const int ShortSide = 360;

    /// <summary>Width of one frame on the sheet, pixels.</summary>
    public const int FrameWidth = 240;

    /// <summary>Frames side by side on a sheet of a landscape video.</summary>
    public const int ColumnsLandscape = 4;

    /// <summary>Frames side by side on a sheet of a portrait or square video.</summary>
    public const int ColumnsPortrait = 6;

    /// <summary>Where in its beat a key moment lies (share of the beat, the actions have played by then).</summary>
    public const double BeatShare = 0.6;

    /// <summary>A key moment lies at most this long after its beat starts (seconds).</summary>
    public const double BeatMaxSeconds = 1.5;

    /// <summary>The grey of a placeholder image (0–255).</summary>
    public const byte PlaceholderGrey = 128;

    /// <summary>The lighter grey of a placeholder's frame (0–255).</summary>
    public const byte PlaceholderEdge = 176;
}
