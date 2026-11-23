namespace TMConcept.Core.Styles;

/// <summary>
/// The material layer the app lays over the whole picture of a style (0.1.32; drawn by the app's page, never by the model's code, still and
/// faint, under the subtitles; new values are added at the end).
/// </summary>
public enum StyleTexture
{
    /// <summary>None.</summary>
    None = 0,

    /// <summary>Paper fibres and a soft vignette.</summary>
    Paper = 1,

    /// <summary>Fine film grain.</summary>
    Grain = 2,

    /// <summary>CRT scanlines and a vignette.</summary>
    Scanlines = 3,

    /// <summary>A halftone dot screen.</summary>
    Halftone = 4,

    /// <summary>A faint technical grid.</summary>
    Grid = 5,
}
