namespace TMConcept.Core.Music;

/// <summary>
/// The Openverse audio search (0.1.45, S6; no key): a page of tracks whose licence allows commercial use with changes (CC0, CC BY, CC BY-SA),
/// and the download of the one the user picked. Called only when the user searches or picks.
/// </summary>
public interface IMusicSearch
{
    /// <summary>Page <paramref name="page"/> (1-based) of the tracks for <paramref name="query"/>.</summary>
    Task<MusicSearchPage> SearchAsync(string query, int page, CancellationToken cancellationToken);

    /// <summary>Downloads <paramref name="track"/> into <paramref name="folder"/>; returns the file's full path.</summary>
    Task<string> DownloadAsync(MusicTrack track, string folder, CancellationToken cancellationToken);
}
