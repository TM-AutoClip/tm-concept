namespace TMConcept.Core.Styles;

/// <summary>Fixed sizes of what the app draws for a template (shared by the input the code reads, the prompt and the overlay).</summary>
public static class StyleLayout
{
    /// <summary>Safe margin on every side, as a fraction of the shorter side.</summary>
    public const double SafeMargin = 0.08;

    /// <summary>Space kept free at the bottom for the subtitles, as a fraction of the height.</summary>
    public const double SubtitleBottom = 0.22;

    /// <summary>Seconds at the end of the video the closing card is shown (in a short video at most its last 40 %, so it never covers the whole video).</summary>
    public const double EndCardSeconds = 3.5;

    /// <summary>Fade-in of the closing card.</summary>
    public const double EndCardFadeSeconds = 0.4;

    /// <summary>Height of the progress bar, as a fraction of the height (at least 6 pixels).</summary>
    public const double ProgressBarHeight = 0.006;
}
