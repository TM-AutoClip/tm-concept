namespace TMConcept.Core.Styles;

/// <summary>Where a title is anchored inside the safe area, as fractions (0 = left / top, 0.5 = centre, 1 = right / bottom).</summary>
/// <param name="X">Horizontal fraction.</param>
/// <param name="Y">Vertical fraction.</param>
/// <param name="Row">Grid row, 0–2 from the top.</param>
/// <param name="Column">Grid column, 0–2 from the left.</param>
public readonly record struct TitleAnchor(double X, double Y, int Row, int Column);
