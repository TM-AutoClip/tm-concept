using System.Text.RegularExpressions;
using TMConcept.Core.Render;

namespace TMConcept.Core.Engines;

/// <summary>
/// The static check that a video's code shows its scene images (20 Nov 2026; a small local model copied the repair example, which drew no
/// image, and the video showed only a coloured background although every scene had one). It runs only when the input has images and only
/// asks for the engine's own way of reading <c>scene.image</c> and putting it on screen: HyperFrames an <c>img</c> element, a CSS background
/// (<c>backgroundImage</c>, or <c>url(…)</c> in <c>background</c> / <c>cssText</c>) or an SVG <c>image</c> with an <c>href</c> (the last two
/// since the 0.1.30 review: valid designs were refused), Remotion <c>Img</c> / <c>staticFile</c>, Revideo <c>Img</c>. It is kept loose on purpose: a
/// finding costs a repair, and the frame check after the render (<see cref="Videos.FrameCheck"/>) still sees what is really shown.
/// </summary>
public static partial class ImageUseCheck
{
    /// <summary>The start of the finding (the repair prompt names its fix by it).</summary>
    public const string FindingStart = "Scene images are never shown";

    /// <summary>The finding when <paramref name="input"/> has scene images and <paramref name="code"/> does not show them; null otherwise.</summary>
    public static string? Finding(EngineKind engine, string? code, RenderInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!input.Scenes.Any(s => s.Image.Length > 0) || Shows(engine, code ?? string.Empty))
        {
            return null;
        }

        return engine switch
        {
            EngineKind.Remotion => FindingStart + ": read each scene's scene.image and show it full frame with <Img src={staticFile(scene.image)} />.",
            EngineKind.Revideo => FindingStart + ": read each scene's scene.image and show it full frame with <Img src={'/' + scene.image} />.",
            _ => FindingStart + ": read each scene's scene.image and show it full frame in an img element (picture.src = scene.image).",
        };
    }

    /// <summary>Whether <paramref name="code"/> reads the scene image and has the engine's way of showing a picture.</summary>
    public static bool Shows(EngineKind engine, string code)
    {
        ArgumentNullException.ThrowIfNull(code);
        if (!ReadsImage().IsMatch(code))
        {
            return false;
        }

        return engine switch
        {
            EngineKind.Remotion => RemotionPicture().IsMatch(code),
            EngineKind.Revideo => RevideoPicture().IsMatch(code),
            _ => HyperFramesPicture().IsMatch(code),
        };
    }

    /// <summary><c>.image</c> read as a property, or <c>image</c> taken out of a scene by destructuring.</summary>
    [GeneratedRegex(@"\.image\b|[{,]\s*image\s*[,}:]")]
    private static partial Regex ReadsImage();

    [GeneratedRegex(@"createElement\s*\(\s*[""'`]img[""'`]|\.src\s*=|setAttribute(NS)?\s*\([^)]*?[""'`](xlink:)?(src|href)[""'`]|\.href\b|backgroundImage|background-image|\burl\s*\(|\bsrc\s*:", RegexOptions.IgnoreCase)]
    private static partial Regex HyperFramesPicture();

    [GeneratedRegex(@"<\s*(Img|img)\b|staticFile\s*\(")]
    private static partial Regex RemotionPicture();

    [GeneratedRegex(@"<\s*Img\b|new\s+Img\s*\(")]
    private static partial Regex RevideoPicture();
}
