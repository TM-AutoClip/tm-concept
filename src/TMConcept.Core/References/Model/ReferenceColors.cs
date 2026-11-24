namespace TMConcept.Core.References;

/// <summary>
/// The colour of a reference (<see cref="ReferenceColorReader"/>): the mean CIELAB, the mean brightness and saturation (0–1) and its main
/// colours (#RRGGBB, most of the picture first).
/// </summary>
public sealed record ReferenceColors(double L, double A, double B, double Brightness, double Saturation, IReadOnlyList<string> Palette);
