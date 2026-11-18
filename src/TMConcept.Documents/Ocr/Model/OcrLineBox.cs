namespace TMConcept.Documents.Ocr;

/// <summary>Where the detector found a line of text on a decoded page image (pixels, upright box).</summary>
public sealed record OcrLineBox(int X, int Y, int Width, int Height);
