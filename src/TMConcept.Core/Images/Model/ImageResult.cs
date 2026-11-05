namespace TMConcept.Core.Images;

/// <summary>An image file and its price (catalog price per image; providers do not report it).</summary>
public sealed record ImageResult(string File, decimal CostUsd);
