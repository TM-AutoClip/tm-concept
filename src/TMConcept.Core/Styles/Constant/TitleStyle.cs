namespace TMConcept.Core.Styles;

/// <summary>How the scene titles of a template are drawn (new values are added at the end; stored as numbers).</summary>
public enum TitleStyle
{
    /// <summary>Plain text in the text colour.</summary>
    Plain = 0,

    /// <summary>Text on a full-width band in the surface colour.</summary>
    Band = 1,

    /// <summary>Text with an outline in the background colour.</summary>
    Outline = 2,

    /// <summary>Text with a short accent line under it.</summary>
    Underline = 3,

    /// <summary>Text in a rounded box in the accent colour.</summary>
    Boxed = 4,
}
