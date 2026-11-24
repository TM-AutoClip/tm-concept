namespace TMConcept.Core.Videos;

/// <summary>
/// The background music of a run, checked before anything is paid for (0.1.44): the track copied into the work folder, its length as ffmpeg
/// read it and its line for the version's asset record.
/// </summary>
/// <param name="File">The copy in the work folder (full path).</param>
/// <param name="DurationMs">The track's length.</param>
/// <param name="Entry">Its line in <see cref="VideoFolders.AssetsFile"/>.</param>
public sealed record PreparedMusic(string File, long DurationMs, AssetEntry Entry)
{
    /// <summary>
    /// The library track is chosen later by the concept (0.1.45, C4): the library is installed, <see cref="File"/> is still empty and
    /// <see cref="VideoMusicStep.SettleAsync"/> chooses from the scene plan's music hints.
    /// </summary>
    public bool Automatic { get; init; }

    /// <summary>How the track is laid under the video (0.1.45, F7 + F8; null = as it is, from its start, looped when short).</summary>
    public MusicPlacement? Placement { get; init; }
}
