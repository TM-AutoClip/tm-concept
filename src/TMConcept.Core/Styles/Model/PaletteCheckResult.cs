namespace TMConcept.Core.Styles;

/// <summary>The contrast check of a palette: problems that block saving and warnings that do not.</summary>
/// <param name="Errors">Blocking problems (a colour that is not one, text that cannot be read).</param>
/// <param name="Warnings">Weak contrasts worth a look (accent, quiet text).</param>
/// <param name="TextContrast">Contrast of the text on the background (0 when a colour is not valid).</param>
public sealed record PaletteCheckResult(IReadOnlyList<PaletteIssue> Errors, IReadOnlyList<PaletteIssue> Warnings, double TextContrast)
{
    /// <summary>The palette can be used.</summary>
    public bool Ok => Errors.Count == 0;
}
