namespace TMConcept.Core.Styles;

/// <summary>A colour read from code (<see cref="PaletteColors.TryParse"/>).</summary>
/// <param name="R">Red, 0–255.</param>
/// <param name="G">Green, 0–255.</param>
/// <param name="B">Blue, 0–255.</param>
/// <param name="A">Opacity, 0–1 (1 when the literal has none).</param>
public readonly record struct ColorValue(byte R, byte G, byte B, double A);
