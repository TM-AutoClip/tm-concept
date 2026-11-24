namespace TMConcept.Core.Music;

/// <summary>The ready library's catalog (<c>catalog/music.json</c>, embedded in the app; 0.1.45).</summary>
public sealed class MusicCatalogFile
{
    /// <summary>Its format (<see cref="MusicLibraryRules.CatalogSchema"/>).</summary>
    public int Schema { get; set; }

    /// <summary>Its version: the library's folder on disk, so a new list is a new install.</summary>
    public string Version { get; set; } = string.Empty;

    /// <summary>Where the data was read.</summary>
    public string Source { get; set; } = string.Empty;

    /// <summary>The tracks.</summary>
    public List<MusicTrack> Tracks { get; set; } = [];
}
