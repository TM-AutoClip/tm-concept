namespace TMConcept.Core.Videos;

/// <summary>What an <see cref="AssetEntry"/> is (0.1.44, F15; stored as text in <see cref="VideoFolders.AssetsFile"/>).</summary>
public static class AssetKinds
{
    /// <summary>Background music.</summary>
    public const string Music = "music";

    /// <summary>A picture from outside (a library or the user).</summary>
    public const string Image = "image";
}
