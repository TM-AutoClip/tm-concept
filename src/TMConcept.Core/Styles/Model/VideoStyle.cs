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

    /// <summary>
    /// Readable text (0.1.24, on by default; a template saved before has it on too): subtitles and on-screen text get an outline and a soft
    /// shadow in the colour opposite to the text colour (<see cref="PaletteColors.Opposite"/>), so text stays readable over an image or a
    /// scene of its own colour.
    /// </summary>
    public bool ReadableText { get; set; } = true;

    /// <summary>
    /// Font id from <see cref="VideoFonts"/>, or an installed font's (<see cref="SystemFonts"/>, "system:Times New Roman") for titles,
    /// on-screen text and subtitles.
    /// </summary>
    public string FontId { get; set; } = VideoFonts.DefaultId;

    /// <summary>Weight and slant of the text (0.1.24; <see cref="FontEmphasis.Auto"/> = the look before).</summary>
    public FontEmphasis FontEmphasis { get; set; } = FontEmphasis.Auto;

    /// <summary>How titles are drawn.</summary>
    public TitleStyle TitleStyle { get; set; } = TitleStyle.Plain;

    /// <summary>Where titles sit (3×3 grid).</summary>
    public TitlePosition TitlePosition { get; set; } = TitlePosition.TopCenter;

    /// <summary>Colour palette id from <see cref="VideoPalettes"/>, or <see cref="VideoPalettes.CustomId"/> for the template's own colours.</summary>
    public string PaletteId { get; set; } = VideoPalettes.DefaultId;

    /// <summary>
    /// The template's own colours when <see cref="PaletteId"/> is <see cref="VideoPalettes.CustomId"/> (0.1.24); <c>null</c> otherwise. A
    /// copy, also when they came from a saved palette.
    /// </summary>
    public VideoPalette? CustomPalette { get; set; }

    /// <summary>The name of the saved palette the own colours came from (shown in the editor; empty when they were made here).</summary>
    public string CustomPaletteName { get; set; } = string.Empty;

    /// <summary>
    /// The look of the video (<see cref="DrawStyles"/>): how scenes without an image are drawn and how images are asked for. A new template
    /// starts on <see cref="DrawStyle.Auto"/> (0.1.32: no style given = the model chooses one that fits each video's topic).
    /// </summary>
    public DrawStyle DrawStyle { get; set; } = DrawStyle.Auto;

    /// <summary>
    /// The style the model chose for this video when <see cref="DrawStyle"/> is <see cref="DrawStyle.Auto"/> and no preset fitted: its own short
    /// description (0.1.32). Only ever set on a video's snapshot (<see cref="Videos.VideoOptions.Style"/>), never on a saved template; a chosen
    /// preset is written into <see cref="DrawStyle"/> of the snapshot instead. Empty otherwise.
    /// </summary>
    public string ChosenStyle { get; set; } = string.Empty;

    /// <summary>How much things move.</summary>
    public MotionIntensity Motion { get; set; } = MotionIntensity.Normal;

    /// <summary>
    /// The narration's breathing room (0.1.31): the silence after every sentence and between scenes (<see cref="Speech.NarrationTempos"/>). A
    /// template saved before has none in its JSON and reads as the default.
    /// </summary>
    public Speech.NarrationTempo Tempo { get; set; } = Speech.NarrationTempos.Default;

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
