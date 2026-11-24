namespace TMConcept.Core.Music;

/// <summary>
/// The ready music library on this computer (0.1.45, C4): the tracks of the embedded catalog and, once the user agreed to its download (the
/// <c>music-library</c> component), their files. Nothing here uses the network.
/// </summary>
public interface IMusicLibrary
{
    /// <summary>Every track of the catalog is on this computer.</summary>
    bool Ready { get; }

    /// <summary>The catalog's tracks.</summary>
    IReadOnlyList<MusicTrack> Tracks { get; }

    /// <summary>The library's folder.</summary>
    string Folder { get; }

    /// <summary>The folder of the tracks the user picked from an Openverse search.</summary>
    string PickedFolder { get; }

    /// <summary>The file of a library track on this computer; <c>null</c> when it is not a library track or not there.</summary>
    string? FileOf(MusicTrack track);
}
