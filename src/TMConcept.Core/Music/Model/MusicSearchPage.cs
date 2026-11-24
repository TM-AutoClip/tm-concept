namespace TMConcept.Core.Music;

/// <summary>One page of an Openverse search (S6): the tracks that passed the licence check, the page and how many pages there are.</summary>
/// <param name="Tracks">The usable tracks of the page.</param>
/// <param name="Page">The page (1-based).</param>
/// <param name="PageCount">How many pages there are.</param>
/// <param name="Dropped">Results left out because their licence does not allow commercial use and changes (or is not known).</param>
public sealed record MusicSearchPage(IReadOnlyList<MusicTrack> Tracks, int Page, int PageCount, int Dropped);
