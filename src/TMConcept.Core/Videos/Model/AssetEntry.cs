namespace TMConcept.Core.Videos;

/// <summary>
/// One outside file a video uses (0.1.44, F15): a line of the version's <see cref="VideoFolders.AssetsFile"/>. Written by the app; the
/// attribution is what the user (or a library's record) gave.
/// </summary>
public sealed class AssetEntry
{
    /// <summary>What it is (<see cref="AssetKinds"/>).</summary>
    public string Kind { get; set; } = AssetKinds.Music;

    /// <summary>Its name (the track's title, or the file's name without its folder).</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Who made it (empty when not known).</summary>
    public string Artist { get; set; } = string.Empty;

    /// <summary>Where it came from (a web address; empty for the user's own file).</summary>
    public string SourceUrl { get; set; } = string.Empty;

    /// <summary>Its licence ("CC BY 4.0", <see cref="AssetLicenses.OwnFile"/>…).</summary>
    public string License { get; set; } = string.Empty;

    /// <summary>The credit it asks for, ready to paste into the video's description (empty = none asked).</summary>
    public string Attribution { get; set; } = string.Empty;

    /// <summary>The file's name in the version folder (inside <see cref="VideoFolders.Music"/> for music).</summary>
    public string File { get; set; } = string.Empty;
}
