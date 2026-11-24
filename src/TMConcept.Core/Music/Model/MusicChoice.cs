namespace TMConcept.Core.Music;

/// <summary>The track <see cref="MusicMatcher"/> chose, its score and whether its style was the asked one or only the nearest.</summary>
/// <param name="Track">The track.</param>
/// <param name="Score">How well it fits (higher is better).</param>
/// <param name="Nearest">No track had the asked style: the nearest style was taken.</param>
public sealed record MusicChoice(MusicTrack Track, int Score, bool Nearest);
