namespace TMConcept.Core.Media;

/// <summary>A small RGB copy of a region of a picture or video frame (<see cref="IMediaTools.SampleAsync"/>): 3 bytes per pixel, row by row.</summary>
/// <param name="Width">Width in pixels.</param>
/// <param name="Height">Height in pixels.</param>
/// <param name="Rgb">The pixels (red, green, blue), <c>Width × Height × 3</c> bytes.</param>
public sealed record FramePixels(int Width, int Height, ReadOnlyMemory<byte> Rgb);
