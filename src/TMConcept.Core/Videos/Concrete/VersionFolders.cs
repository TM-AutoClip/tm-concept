using System.Globalization;

namespace TMConcept.Core.Videos;

/// <summary>Names and numbers of the version folders of a made video (<see cref="VideoFolders"/>: v1, v2, … inside the concept's folder).</summary>
public static class VersionFolders
{
    /// <summary>A version's folder name.</summary>
    public static string Version(int number) => VideoFolders.VersionPrefix + number.ToString(CultureInfo.InvariantCulture);

    /// <summary>The number of a version folder name ("v3" → 3); null when the name is not one.</summary>
    public static int? NumberOf(string? folderName) =>
        folderName is { Length: > 1 } name && name.StartsWith(VideoFolders.VersionPrefix, StringComparison.Ordinal)
            && int.TryParse(name.AsSpan(VideoFolders.VersionPrefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var number) && number > 0
            ? number
            : null;

    /// <summary>The folder of the next version inside a concept's folder (one more than the highest there).</summary>
    public static string Next(string conceptFolder)
    {
        var highest = Directory.Exists(conceptFolder)
            ? Directory.EnumerateDirectories(conceptFolder).Select(d => NumberOf(Path.GetFileName(d)) ?? 0).DefaultIfEmpty(0).Max()
            : 0;
        return Path.Combine(conceptFolder, Version(highest + 1));
    }

    /// <summary>The scene clip of scene <paramref name="number"/> inside a version folder.</summary>
    public static string Clip(string versionFolder, int number) =>
        Path.Combine(versionFolder, VideoFolders.Audio, number.ToString(CultureInfo.InvariantCulture) + ".wav");
}
