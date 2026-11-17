namespace TMConcept.Core.Styles;

/// <summary>
/// A template ("Şablon" in the interface; the code says VideoStyle because "template" already means <see cref="Templates.ChannelTemplate"/>):
/// how a video looks and ends. Templates are a global list (not tied to profiles, at most <see cref="VideoStyleLimits.Max"/>) in the
/// encrypted database (<see cref="IVideoStyleStore"/>). When a queued video starts, a copy goes into <see cref="Videos.VideoOptions.Style"/>
/// (<see cref="VideoStyleRules.Snapshot"/>); later edits change only the next video. The settings are enforced by the app (overlays it draws,
/// fonts it copies, colour tokens, the style check), not only asked for in the prompt. Enums are stored as numbers; the palette and font are
/// ids so a removed one falls back to the default (<see cref="VideoStyleRules.Palette"/>, <see cref="VideoStyleRules.Font"/>).
/// </summary>
public sealed class VideoStyle
{
    /// <summary>Id.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Name the user gives it (at most <see cref="VideoStyleLimits.NameMax"/> characters).</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Subtitles burned into the picture (when off, the code never sees the narration and may not draw it).</summary>
    public bool Subtitles { get; set; } = true;

    /// <summary>Progress bar drawn by the app (off by default; when off, the code may not draw a progress bar, timer or scene counter).</summary>
    public bool ProgressBar { get; set; }

    /// <summary>Font id from <see cref="VideoFonts"/> (titles, on-screen text and subtitles).</summary>
    public string FontId { get; set; } = VideoFonts.DefaultId;

    /// <summary>How titles are drawn.</summary>
    public TitleStyle TitleStyle { get; set; } = TitleStyle.Plain;

    /// <summary>Where titles sit (3×3 grid).</summary>
    public TitlePosition TitlePosition { get; set; } = TitlePosition.TopCenter;

    /// <summary>Colour palette id from <see cref="VideoPalettes"/>.</summary>
    public string PaletteId { get; set; } = VideoPalettes.DefaultId;

    /// <summary>Drawing style when there are no images (also steers the image prompt).</summary>
    public DrawStyle DrawStyle { get; set; } = DrawStyle.Flat;

    /// <summary>How much things move.</summary>
    public MotionIntensity Motion { get; set; } = MotionIntensity.Normal;

    /// <summary>Where the closing call to action goes.</summary>
    public ClosingPlacement Closing { get; set; } = ClosingPlacement.Off;

    /// <summary>The closing sentence.</summary>
    public ClosingPhrase ClosingPhrase { get; set; } = ClosingPhrase.WhatsNext;

    /// <summary>The platform whose wording the closing uses (subscribe or follow).</summary>
    public ClosingPlatform ClosingPlatform { get; set; } = ClosingPlatform.YouTube;

    /// <summary>Created (UTC).</summary>
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Last change (UTC).</summary>
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
