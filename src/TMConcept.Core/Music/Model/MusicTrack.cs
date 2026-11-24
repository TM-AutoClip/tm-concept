namespace TMConcept.Core.Music;

/// <summary>
/// One piece of background music the app can use (0.1.45, C4): a track of the ready library (<c>catalog/music.json</c>, Incompetech) or one
/// the user picked from an Openverse search (S6). The licence and the attribution come from the source; the tags are the library's own words
/// (<see cref="MusicTags"/>) and are empty for an Openverse track. Stored in a template's JSON when the user picked it.
/// </summary>
public sealed class MusicTrack
{
    /// <summary>Its id: <c>incompetech:&lt;name&gt;</c> or <c>openverse:&lt;id&gt;</c>.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Its title.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Who made it.</summary>
    public string Artist { get; set; } = string.Empty;

    /// <summary>Its page at the source (where its licence is stated).</summary>
    public string PageUrl { get; set; } = string.Empty;

    /// <summary>The address of the file.</summary>
    public string DownloadUrl { get; set; } = string.Empty;

    /// <summary>Its licence as people read it ("CC BY 4.0", "CC0 1.0").</summary>
    public string License { get; set; } = string.Empty;

    /// <summary>The credit it asks for (empty for CC0).</summary>
    public string Attribution { get; set; } = string.Empty;

    /// <summary>Its moods (<see cref="MusicTags.Moods"/>).</summary>
    public List<string> Moods { get; set; } = [];

    /// <summary>Its styles (<see cref="MusicTags.Styles"/>).</summary>
    public List<string> Styles { get; set; } = [];

    /// <summary>Its tempo in beats per minute as the source states it (0 = not known).</summary>
    public int Bpm { get; set; }

    /// <summary>Its length in seconds as the source states it (0 = not known).</summary>
    public int DurationSeconds { get; set; }

    /// <summary>The file's size in bytes (0 = not known).</summary>
    public long Bytes { get; set; }

    /// <summary>The file's SHA-256 (the library's tracks only; empty for an Openverse track).</summary>
    public string Sha256 { get; set; } = string.Empty;

    /// <summary>
    /// The file: for a library track its name in the library folder; for a picked Openverse track the full path of its download on this
    /// computer.
    /// </summary>
    public string File { get; set; } = string.Empty;
}
