using System.Text.Json;
using TMConcept.Core.Profiles;
using TMConcept.Core.Render;
using TMConcept.Core.Videos;

namespace TMConcept.Core.Styles;

/// <summary>
/// The pure rules of templates, shared by the settings page, the queue and the render step: which palette, font and closing a template
/// resolves to, what can be saved, and the snapshot a video takes when it starts. A video without a snapshot (made before templates) keeps
/// its old behaviour: its <see cref="VideoOptions.Subtitles"/> decides, no progress bar, the default look.
/// </summary>
public static class VideoStyleRules
{
    /// <summary>The template's palette; the default one when there is no template or its id is unknown (a removed palette).</summary>
    public static VideoPalette Palette(VideoStyle? style) => FindPalette(style?.PaletteId) ?? VideoPalettes.All[0];

    /// <summary>A palette by id; <c>null</c> when unknown.</summary>
    public static VideoPalette? FindPalette(string? id) => VideoPalettes.All.FirstOrDefault(p => p.Id == id);

    /// <summary>
    /// The template's font for a video language: the default when there is no template or its id is unknown, and
    /// <see cref="VideoFonts.CyrillicFallbackId"/> when the language is written in Cyrillic and the font cannot draw it.
    /// </summary>
    public static VideoFont Font(VideoStyle? style, string language)
    {
        var font = FindFont(style?.FontId) ?? FindFont(VideoFonts.DefaultId)!;
        return !font.Cyrillic && language == ContentLanguages.Russian ? FindFont(VideoFonts.CyrillicFallbackId)! : font;
    }

    /// <summary>A font by id; <c>null</c> when unknown.</summary>
    public static VideoFont? FindFont(string? id) => VideoFonts.All.FirstOrDefault(f => f.Id == id);

    /// <summary>The verb a platform uses: YouTube subscribe, Instagram and TikTok follow.</summary>
    public static ClosingWording Wording(ClosingPlatform platform) =>
        platform == ClosingPlatform.YouTube ? ClosingWording.Subscribe : ClosingWording.Follow;

    /// <summary>The closing sentences of a language; <c>null</c> when <see cref="ClosingPhrases"/> has none.</summary>
    public static ClosingLanguage? ClosingFor(string language) => ClosingPhrases.All.FirstOrDefault(l => l.Code == language);

    /// <summary>
    /// The closing of a video in its language. <c>null</c> when there is no template, its closing is off, or the language has no sentences
    /// (<see cref="ClosingMissing"/> tells the last case apart so the caller can log a warning; the video is still made).
    /// </summary>
    public static ClosingText? Closing(VideoStyle? style, string language)
    {
        if (style is null || style.Closing == ClosingPlacement.Off || ClosingFor(language) is not { } table)
        {
            return null;
        }

        var line = table.Lines.FirstOrDefault(l => l.Phrase == style.ClosingPhrase) ?? table.Lines[0];
        var follow = Wording(style.ClosingPlatform) == ClosingWording.Follow;
        return new ClosingText(follow ? line.Follow : line.Subscribe, follow ? table.FollowLabel : table.SubscribeLabel,
            Voice: style.Closing is ClosingPlacement.Voice or ClosingPlacement.Both,
            Screen: style.Closing is ClosingPlacement.Screen or ClosingPlacement.Both);
    }

    /// <summary>The template asks for a closing but the language has no sentences (the closing is skipped with a warning).</summary>
    public static bool ClosingMissing(VideoStyle? style, string language) =>
        style is not null && style.Closing != ClosingPlacement.Off && ClosingFor(language) is null;

    /// <summary>
    /// The old three-colour theme of the input file, filled from the palette, so code that reads <c>theme</c> (kept code made before
    /// templates) follows the template too.
    /// </summary>
    public static RenderTheme Theme(VideoPalette palette)
    {
        ArgumentNullException.ThrowIfNull(palette);
        return new RenderTheme { Background = palette.Background, Accent = palette.Accent, Text = palette.Text };
    }

    /// <summary>Why the template cannot be saved; empty when it can.</summary>
    public static IReadOnlyList<VideoStyleProblem> Problems(VideoStyle style)
    {
        ArgumentNullException.ThrowIfNull(style);
        var problems = new List<VideoStyleProblem>();
        var name = style.Name?.Trim() ?? string.Empty;
        if (name.Length == 0)
        {
            problems.Add(VideoStyleProblem.NameMissing);
        }
        else if (name.Length > VideoStyleLimits.NameMax)
        {
            problems.Add(VideoStyleProblem.NameTooLong);
        }

        if (FindPalette(style.PaletteId) is null)
        {
            problems.Add(VideoStyleProblem.UnknownPalette);
        }

        if (FindFont(style.FontId) is null)
        {
            problems.Add(VideoStyleProblem.UnknownFont);
        }

        if (!Enum.IsDefined(style.TitleStyle) || !Enum.IsDefined(style.TitlePosition) || !Enum.IsDefined(style.DrawStyle) ||
            !Enum.IsDefined(style.Motion) || !Enum.IsDefined(style.Closing) || !Enum.IsDefined(style.ClosingPhrase) ||
            !Enum.IsDefined(style.ClosingPlatform))
        {
            problems.Add(VideoStyleProblem.UnknownValue);
        }

        return problems;
    }

    /// <summary>A deep copy (through the source-generated JSON, so a field added later is copied too).</summary>
    public static VideoStyle Copy(VideoStyle style)
    {
        ArgumentNullException.ThrowIfNull(style);
        var json = JsonSerializer.Serialize(style, CoreJsonContext.Default.VideoStyle);
        return JsonSerializer.Deserialize(json, CoreJsonContext.Default.VideoStyle)
            ?? throw new InvalidOperationException("The template could not be copied.");
    }

    /// <summary>
    /// Takes the template into the options of a video that is starting: a copy (later edits change only the next video) and
    /// <see cref="VideoOptions.Subtitles"/> set from it, so code that reads only the old field agrees.
    /// </summary>
    public static void Snapshot(VideoOptions options, VideoStyle style)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(style);
        options.Style = Copy(style);
        options.Subtitles = style.Subtitles;
    }

    /// <summary>Subtitles are on for this video: the snapshot decides; a video without one keeps its old field.</summary>
    public static bool SubtitlesOn(VideoOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return options.Style?.Subtitles ?? options.Subtitles;
    }

    /// <summary>The app draws a progress bar on this video (never for a video without a snapshot).</summary>
    public static bool ProgressBarOn(VideoOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return options.Style?.ProgressBar ?? false;
    }
}
