namespace TMConcept.Core.Styles;

/// <summary>
/// The weight and slant of a template's text (0.1.24; stored as a number, new values only at the end). <see cref="Auto"/> is the look before:
/// bold titles, regular on-screen text, bold subtitles when the font has a bold weight. Any other value is used for every text (titles,
/// on-screen text and subtitles). A font without an italic file is slanted by the renderer.
/// </summary>
public enum FontEmphasis
{
    /// <summary>Bold titles, regular text, bold subtitles (the look before 0.1.24).</summary>
    Auto = 0,

    /// <summary>Regular everywhere.</summary>
    Regular = 1,

    /// <summary>Bold everywhere.</summary>
    Bold = 2,

    /// <summary>Italic everywhere.</summary>
    Italic = 3,

    /// <summary>Bold italic everywhere.</summary>
    BoldItalic = 4,
}
