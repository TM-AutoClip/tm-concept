namespace TMConcept.Core.Videos;

/// <summary>
/// The background music of a run, checked before anything is paid for (0.1.44): the track copied into the work folder, its length as ffmpeg
/// read it and its line for the version's asset record.
/// </summary>
/// <param name="File">The copy in the work folder (full path).</param>
/// <param name="DurationMs">The track's length.</param>
/// <param name="Entry">Its line in <see cref="VideoFolders.AssetsFile"/>.</param>
public sealed record PreparedMusic(string File, long DurationMs, AssetEntry Entry);
