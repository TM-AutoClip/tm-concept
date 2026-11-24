namespace TMConcept.Core.Videos;

/// <summary>A frame on the review sheet (F11, 0.1.50): its scene, its moment in the video (seconds) and which moment it is (start, middle, end, beat).</summary>
public sealed record QualityFrame(int Scene, double Seconds, string Moment);
