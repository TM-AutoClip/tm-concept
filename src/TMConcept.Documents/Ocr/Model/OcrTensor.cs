namespace TMConcept.Documents.Ocr;

/// <summary>A tensor of the local OCR's networks: its values, row-major, and its shape (e.g. [1, 3, height, width]).</summary>
public sealed record OcrTensor(float[] Data, int[] Shape);
