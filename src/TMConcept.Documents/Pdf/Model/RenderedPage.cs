namespace TMConcept.Documents.Pdf;

/// <summary>A drawn PDF page (0.1.27): its size in pixels and its pixels, four bytes each (blue, green, red, unused), row by row.</summary>
public sealed record RenderedPage(int Width, int Height, byte[] Bgrx);
