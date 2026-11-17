namespace TMConcept.Core.Styles;

/// <summary>A colour palette of the templates: one #RRGGBB value per <see cref="PaletteTokens"/> token.</summary>
/// <param name="Id">Stable id stored in a template ("amber-navy"); the interface names it with the key <c>style.palette.&lt;id&gt;</c>.</param>
/// <param name="Background">Page background.</param>
/// <param name="Surface">Cards, bands and panels.</param>
/// <param name="Accent">The main accent.</param>
/// <param name="Accent2">The second accent.</param>
/// <param name="Text">Text.</param>
/// <param name="Muted">Quiet text and lines.</param>
/// <param name="Light">A light palette (dark text on a light background).</param>
public sealed record VideoPalette(string Id, string Background, string Surface, string Accent, string Accent2, string Text, string Muted, bool Light);
