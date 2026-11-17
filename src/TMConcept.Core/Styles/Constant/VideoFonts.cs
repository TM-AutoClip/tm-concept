namespace TMConcept.Core.Styles;

/// <summary>
/// The fonts a template can pick. Every one is SIL OFL 1.1 from github.com/google/fonts and draws the Turkish letters (ç ğ ı İ ö ş ü,
/// â î û) and the other Latin letters of the output languages; only Fira Sans draws Cyrillic, so a Russian video with another font uses
/// <see cref="CyrillicFallbackId"/> (<see cref="VideoStyleRules.Font"/>). Static weights only (no variable fonts: the subtitle renderer and
/// the engines pick the weight by file). Ids are stored in templates and never change; a font is only ever added. The font files and their
/// OFL.txt ship with the app (render step).
/// </summary>
public static class VideoFonts
{
    /// <summary>Id of the default font (a plain sans serif close to the look before templates).</summary>
    public const string DefaultId = "fira-sans";

    /// <summary>The font used when the picked one cannot draw Cyrillic.</summary>
    public const string CyrillicFallbackId = DefaultId;

    /// <summary>SPDX id of the licence of every font here.</summary>
    public const string Licence = "OFL-1.1";

    /// <summary>Every font, in the order of the template editor.</summary>
    public static readonly IReadOnlyList<VideoFont> All =
    [
        new(DefaultId, "Fira Sans", "FiraSans-Regular.ttf", "FiraSans-Bold.ttf", Licence, "ofl/firasans", Cyrillic: true),
        new("poppins", "Poppins", "Poppins-Regular.ttf", "Poppins-Bold.ttf", Licence, "ofl/poppins", Cyrillic: false),
        new("bebas-neue", "Bebas Neue", "BebasNeue-Regular.ttf", null, Licence, "ofl/bebasneue", Cyrillic: false),
        new("anton", "Anton", "Anton-Regular.ttf", null, Licence, "ofl/anton", Cyrillic: false),
        new("crimson-text", "Crimson Text", "CrimsonText-Regular.ttf", "CrimsonText-Bold.ttf", Licence, "ofl/crimsontext", Cyrillic: false),
        new("patrick-hand", "Patrick Hand", "PatrickHand-Regular.ttf", null, Licence, "ofl/patrickhand", Cyrillic: false),
    ];
}
