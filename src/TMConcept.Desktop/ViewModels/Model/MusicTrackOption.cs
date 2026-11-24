namespace TMConcept.Desktop.ViewModels;

/// <summary>A track in the template editor's music list or search results (0.1.45): null = chosen by each video's concept.</summary>
/// <param name="Track">The track; null = automatic.</param>
/// <param name="Label">Its title (and artist).</param>
/// <param name="Detail">Its tags or licence and length.</param>
public sealed record MusicTrackOption(Core.Music.MusicTrack? Track, string Label, string Detail);
