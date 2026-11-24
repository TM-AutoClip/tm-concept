namespace TMConcept.Core.Styles;

/// <summary>Why a template cannot be saved (<see cref="VideoStyleRules.Problems"/>; the interface shows its own text for each).</summary>
public enum VideoStyleProblem
{
    /// <summary>The name is empty.</summary>
    NameMissing = 0,

    /// <summary>The name is longer than <see cref="VideoStyleLimits.NameMax"/>.</summary>
    NameTooLong = 1,

    /// <summary>The palette id is not in <see cref="VideoPalettes"/>.</summary>
    UnknownPalette = 2,

    /// <summary>The font id is not in <see cref="VideoFonts"/>.</summary>
    UnknownFont = 3,

    /// <summary>An enum field holds a value this version does not know.</summary>
    UnknownValue = 4,

    /// <summary>The template's own colours cannot be read well (<see cref="PaletteDesigner.Check"/>).</summary>
    PaletteContrast = 5,

    /// <summary>"Own file" music is chosen but no file is (0.1.44).</summary>
    MusicFileMissing = 6,
}
