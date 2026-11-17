namespace TMConcept.Core.Media;

/// <summary>A picture laid over the video as a circle at the mux (the channel photo on the closing card).</summary>
/// <param name="File">The picture, a file name inside the mux folder (PNG, JPEG or WebP).</param>
/// <param name="X">Left edge in video pixels.</param>
/// <param name="Y">Top edge in video pixels.</param>
/// <param name="Size">Width and height of the circle (even).</param>
/// <param name="StartSeconds">It fades in from here.</param>
/// <param name="EndSeconds">It stays until here.</param>
/// <param name="FadeSeconds">Length of the fade-in.</param>
/// <param name="Fps">Frames per second of the video.</param>
public sealed record MuxPhoto(string File, int X, int Y, int Size, double StartSeconds, double EndSeconds, double FadeSeconds, int Fps);
