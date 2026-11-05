namespace TMConcept.Engines.Playback;

/// <summary>A video to play: ffmpeg, the file, the decoded picture size and rate, and the length.</summary>
public sealed record PlaybackSource(string Ffmpeg, string File, int Width, int Height, int Fps, long DurationMs);
