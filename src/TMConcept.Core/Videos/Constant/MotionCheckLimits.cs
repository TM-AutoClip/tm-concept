namespace TMConcept.Core.Videos;

/// <summary>
/// The limits of the motion check of a finished video (<see cref="MotionCheck"/>, F3, 0.1.41). The frame size, the grid and the cut, freeze,
/// black, white and hard-end numbers are ReelMimic video-clone motion_check.py (MIT) eşikleri (thresholds; THIRD-PARTY-NOTICES.md); the
/// numbers marked "ours" fit them to this app (the planned fades, the scene starts, the drawer's beats). The check only warns: a false
/// finding costs a look, never the video.
/// </summary>
public static class MotionCheckLimits
{
    /// <summary>Width of the grey frames the check reads (ReelMimic).</summary>
    public const int FrameWidth = 160;

    /// <summary>Height of the grey frames (ReelMimic).</summary>
    public const int FrameHeight = 96;

    /// <summary>Cells across and down the frame for "how much of the frame changed" (8 × 8, ReelMimic).</summary>
    public const int Grid = 8;

    /// <summary>A cell whose mean change (0–1) is above this changed a lot (ReelMimic).</summary>
    public const double CutCellChange = 0.1;

    /// <summary>A cut: more than this share of the cells changed a lot at once (ReelMimic) …</summary>
    public const double CutShare = 0.5;

    /// <summary>… and the whole frame's mean change is above this (ReelMimic).</summary>
    public const double CutChange = 0.1;

    /// <summary>A frame that changed less than this (mean, 0–1) from the one before is still (ReelMimic).</summary>
    public const double FreezeChange = 0.0015;

    /// <summary>Still frames for this long or longer are a freeze (ReelMimic).</summary>
    public const double FreezeSeconds = 1.0;

    /// <summary>A frame darker than this (mean brightness, 0–1) is black (ReelMimic).</summary>
    public const double BlackLuminance = 0.04;

    /// <summary>A frame brighter than this is white (ReelMimic).</summary>
    public const double WhiteLuminance = 0.92;

    /// <summary>White frames for at most this long are a flash; longer is a white card (ReelMimic's 0.5 s spacing of white moments).</summary>
    public const double FlashSeconds = 0.5;

    /// <summary>Black frames this close to the start are the video's own start (ReelMimic).</summary>
    public const double StartSkipSeconds = 0.2;

    /// <summary>The end is looked at over this last stretch (ReelMimic).</summary>
    public const double EndTailSeconds = 0.5;

    /// <summary>A hard cut to black at the end: the stretch starts brighter than this … (ReelMimic).</summary>
    public const double EndTailBright = 0.12;

    /// <summary>… and drops by more than this in one frame (ReelMimic).</summary>
    public const double EndDrop = 0.1;

    /// <summary>A cut this close to a scene start (or the closing card's start) is the scene's own cut (ours).</summary>
    public const double SceneEdgeSeconds = 0.2;

    /// <summary>Around a planned fade this much on each side is the fade's (ours: the fade's half, 0.25 s, and a margin).</summary>
    public const double FadeWindowSeconds = (Scenes.SceneTransitions.FadeHalfMs / 1000.0) + 0.15;

    /// <summary>The brightness before and after a fade is measured over this much (ours).</summary>
    public const double FadeReferenceSeconds = 0.5;

    /// <summary>A fade happened when its darkest frame is below this share of the brightness around it (ours).</summary>
    public const double FadeDip = 0.5;

    /// <summary>Around a darker spot than this a fade cannot be seen, and it is not checked (ours).</summary>
    public const double FadeDarkReference = 0.08;

    /// <summary>A drawer beat with a camera move or an action is expected to move for this long after its start (ours).</summary>
    public const double BeatMotionSeconds = 1.0;

    /// <summary>A freeze is reported when at least this much of it falls where the plan expects motion (ours).</summary>
    public const double FreezeUncoveredSeconds = 0.5;

    /// <summary>At most this many frame strips are written for one video (ours).</summary>
    public const int MaxStrips = 12;

    /// <summary>Frames in one strip (ReelMimic clip_strip).</summary>
    public const int StripFrames = 10;

    /// <summary>A strip of something that lasts (a freeze, a fade, the end) spreads its frames over this long (ours).</summary>
    public const double WideStripSeconds = 2.0;

    /// <summary>Width of one frame in a strip, pixels.</summary>
    public const int StripFrameWidth = 320;
}
