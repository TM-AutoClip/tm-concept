namespace TMConcept.Core.Styles;

/// <summary>What is behind text, measured against the text colour (<see cref="TextBackdrop.Measure"/>).</summary>
/// <param name="Luminance">Mean relative luminance of the region (WCAG 2: 0 = black, 1 = white).</param>
/// <param name="Contrast">Contrast ratio of the text colour against the region's mean luminance (1 to 21).</param>
/// <param name="LowContrastShare">Share of the pixels whose own contrast with the text colour is below <see cref="TextBackdrop.LowContrast"/> (0–1).</param>
/// <param name="Busyness">Mean brightness change between neighbouring pixels (0 = flat, about 0.3 = noise): how busy the region is.</param>
public sealed record BackdropMeasure(double Luminance, double Contrast, double LowContrastShare, double Busyness);
