using Avalonia.Media;
using Avalonia.Platform;
using TMConcept.Core.Styles;

namespace TMConcept.Desktop.Videos;

/// <summary>
/// The font family that shows a video font's name in the font itself (template editor). The regular weights of the bundled video fonts
/// (Core's Styles/Files/Fonts) are linked into the app resources at <see cref="AssetFolder"/> (the file names of
/// <see cref="VideoFont.RegularFile"/>); should a file be missing, the name is drawn in a font installed on the computer with the same family
/// name, or else in the interface font.
/// </summary>
public static class VideoFontPreview
{
    /// <summary>Where the video font files are app resources.</summary>
    public const string AssetFolder = "avares://TMConcept/Assets/VideoFonts";

    /// <summary>The preview family of <paramref name="font"/>.</summary>
    public static FontFamily For(VideoFont font)
    {
        ArgumentNullException.ThrowIfNull(font);
        return AssetLoader.Exists(new Uri(AssetFolder + "/" + font.RegularFile))
            ? new FontFamily(AssetFolder + "#" + font.Family)
            : new FontFamily(font.Family);
    }
}
