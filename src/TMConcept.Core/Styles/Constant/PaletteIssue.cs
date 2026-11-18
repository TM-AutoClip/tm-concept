namespace TMConcept.Core.Styles;

/// <summary>What is wrong with a palette's colours (<see cref="PaletteDesigner.Check"/>; the editor shows its own text for each).</summary>
public enum PaletteIssue
{
    /// <summary>A colour is not a #RRGGBB value.</summary>
    InvalidColour = 0,

    /// <summary>Text on the background is below 4.5:1 (cannot be saved).</summary>
    TextOnBackground = 1,

    /// <summary>Text on the surface (bands, cards) is below 3:1 (cannot be saved).</summary>
    TextOnSurface = 2,

    /// <summary>The accent on the background is below 3:1 (a warning).</summary>
    AccentOnBackground = 3,

    /// <summary>Quiet text on the background is below 3:1 (a warning).</summary>
    MutedOnBackground = 4,
}
