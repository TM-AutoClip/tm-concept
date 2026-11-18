namespace TMConcept.Core.Styles;

/// <summary>The edge of the subtitles in the ASS style (<see cref="StyleOverlay.Edge"/>).</summary>
/// <param name="Outline">Outline colour (<c>&amp;HAABBGGRR</c>).</param>
/// <param name="Back">Shadow colour (<c>&amp;HAABBGGRR</c>).</param>
/// <param name="Width">Outline width in pixels of the video.</param>
/// <param name="Shadow">Shadow distance in pixels (0 = none).</param>
public sealed record SubtitleEdge(string Outline, string Back, int Width, int Shadow);
