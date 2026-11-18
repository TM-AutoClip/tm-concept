namespace TMConcept.Documents.Ocr;

/// <summary>A decoded page image: its size and its pixels, three bytes (red, green, blue) each, row by row.</summary>
public sealed record DecodedImage(int Width, int Height, byte[] Rgb);
