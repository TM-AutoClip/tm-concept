namespace TMConcept.Core.Videos;

/// <summary>Licence words of an <see cref="AssetEntry"/> the app writes itself (0.1.44).</summary>
public static class AssetLicenses
{
    /// <summary>The user's own file: its licence is the user's responsibility, the app does not know it.</summary>
    public const string OwnFile = "own-file";

    /// <summary>The start of the licence of generated music (0.1.46, C5): "generated: &lt;model&gt;, &lt;licence&gt;".</summary>
    public const string GeneratedPrefix = "generated:";

    /// <summary>The start of every Creative Commons Attribution licence (CC BY, CC BY-SA, CC BY-NC…): they ask for credit.</summary>
    public const string CcByPrefix = "CC BY";
}
