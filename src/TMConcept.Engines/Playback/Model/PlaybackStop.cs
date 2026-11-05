namespace TMConcept.Engines.Playback;

/// <summary>Why playing stopped by itself: the end of the video, or an error (its text; empty at the end).</summary>
public sealed record PlaybackStop(bool Ended, string Error, long PositionMs);
