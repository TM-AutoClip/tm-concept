namespace TMConcept.Core.Render;

/// <summary>
/// The video's template resolved for the code (input.json <c>style</c>, written by <see cref="RenderStyleBuilder"/> when the video has a
/// template; absent for videos made before templates). Names are lower-case words so the prompt and the helpers can use them as they are.
/// The app draws the progress bar, the subtitles and the closing card itself (at the mux): the code never draws them.
/// </summary>
public sealed class RenderStyle
{
    /// <summary>The colours (only these may be used).</summary>
    public RenderColors Colors { get; set; } = new();

    /// <summary>The font family (CSS <c>font-family</c>; the app copies its files into the project's <c>fonts/</c> folder).</summary>
    public string Font { get; set; } = string.Empty;

    /// <summary>The regular font file, relative to the project (<c>fonts/FiraSans-Regular.ttf</c>).</summary>
    public string FontRegular { get; set; } = string.Empty;

    /// <summary>The bold font file, relative to the project; empty for a single-weight font.</summary>
    public string FontBold { get; set; } = string.Empty;

    /// <summary>How titles look: plain, band, outline, underline or boxed.</summary>
    public string TitleStyle { get; set; } = string.Empty;

    /// <summary>Title anchor inside the safe area, 0 = left, 0.5 = centre, 1 = right.</summary>
    public double TitleX { get; set; }

    /// <summary>Title anchor inside the safe area, 0 = top, 0.5 = middle, 1 = bottom.</summary>
    public double TitleY { get; set; }

    /// <summary>The drawing style for scenes without an image: flat, stick-figure, line-art, paper-cut, infographic or neon.</summary>
    public string DrawStyle { get; set; } = string.Empty;

    /// <summary>How much moves: calm, normal or lively.</summary>
    public string Motion { get; set; } = string.Empty;

    /// <summary>Subtitles are burned in afterwards (keep the bottom free).</summary>
    public bool Subtitles { get; set; }

    /// <summary>The app draws a progress bar along the top edge.</summary>
    public bool ProgressBar { get; set; }

    /// <summary>Safe margin on every side, as a fraction of the shorter side.</summary>
    public double SafeMargin { get; set; }

    /// <summary>Space kept free at the bottom, as a fraction of the height (the subtitles' place when they are on).</summary>
    public double SafeBottom { get; set; }

    /// <summary>Seconds at the end that the app's closing card covers (0 = none): no new text should start there.</summary>
    public double EndCardSeconds { get; set; }
}
