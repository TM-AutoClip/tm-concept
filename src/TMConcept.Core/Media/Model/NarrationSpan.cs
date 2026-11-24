namespace TMConcept.Core.Media;

/// <summary>A part of the video in which the narration speaks (milliseconds on the video's time line, or in a clip).</summary>
/// <param name="StartMs">Where the speech starts.</param>
/// <param name="EndMs">Where it ends (after <paramref name="StartMs"/>).</param>
public readonly record struct NarrationSpan(long StartMs, long EndMs);
