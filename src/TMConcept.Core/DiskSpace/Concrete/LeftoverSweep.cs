using System.Text.RegularExpressions;

namespace TMConcept.Core.DiskSpace;

/// <inheritdoc cref="ILeftoverSweep" />
public sealed partial class LeftoverSweep : ILeftoverSweep
{
    /// <summary>The start of the name an old components folder got while a reinstall of a version up to 0.1.56 installed into a new one.</summary>
    public const string OldInfix = ".old-";

    /// <summary>The start of the name a new components folder got (up to 0.1.56) when its install did not finish and the old one went back.</summary>
    public const string FailedInfix = ".failed-";

    /// <summary>The start of the name an even older version gave the components folder it set aside during a reinstall.</summary>
    public const string AsideInfix = ".aside-";

    /// <summary>The end of the name of the marker file a reinstall of a version up to 0.1.56 kept next to <c>tools</c>.</summary>
    public const string ReinstallMarkerSuffix = ".reinstalling";

    /// <summary>The end of the name of a folder an archive is unpacked into before it is moved into place.</summary>
    public const string UnpackSuffix = ".unpack";

    /// <summary>The engines' folder inside <c>tools</c>.</summary>
    public const string EnginesFolder = "engines";

    /// <summary>The temporary folder of an engine package.</summary>
    public const string TempFolder = "tmp";

    /// <inheritdoc />
    public IReadOnlyList<string> OldComponentFolders(string tools, IEnumerable<string> folders)
    {
        ArgumentException.ThrowIfNullOrEmpty(tools);
        ArgumentNullException.ThrowIfNull(folders);
        var root = Full(tools);
        var parent = Path.GetDirectoryName(root);
        if (parent is null)
        {
            return [];
        }

        var name = Path.GetFileName(root);
        return folders.Select(Full).Where(f => Same(Path.GetDirectoryName(f), parent) && Matches(name, Path.GetFileName(f)))
            .Distinct(Comparer).Order(StringComparer.Ordinal).ToList();
    }

    /// <inheritdoc />
    public string OldReinstallMarker(string tools)
    {
        ArgumentException.ThrowIfNullOrEmpty(tools);
        return Full(tools) + ReinstallMarkerSuffix;
    }

    /// <inheritdoc />
    public IReadOnlyList<string> Stagings(string tools, IEnumerable<string> folders)
    {
        ArgumentException.ThrowIfNullOrEmpty(tools);
        ArgumentNullException.ThrowIfNull(folders);
        var root = Full(tools);
        var downloads = Path.Combine(root, Components.ComponentFolders.DownloadsFolder);
        return folders.Select(Full).Where(f => Same(f, downloads)
                || (Paths.LibraryPaths.Inside(f, root) && Path.GetFileName(f).EndsWith(UnpackSuffix, Comparison) && Path.GetFileName(f).Length > UnpackSuffix.Length))
            .Distinct(Comparer).Order(StringComparer.Ordinal).ToList();
    }

    /// <inheritdoc />
    public IReadOnlyList<string> EngineTemps(string tools, IEnumerable<string> engineFolders)
    {
        ArgumentException.ThrowIfNullOrEmpty(tools);
        ArgumentNullException.ThrowIfNull(engineFolders);
        var engines = Path.Combine(Full(tools), EnginesFolder);
        return engineFolders.Select(Full).Where(f => Same(Path.GetDirectoryName(f), engines) && Path.GetFileName(f).Length > 0)
            .Select(f => Path.Combine(f, TempFolder)).Distinct(Comparer).Order(StringComparer.Ordinal).ToList();
    }

    /// <inheritdoc />
    public IReadOnlyList<string> ChannelTemps(string work, IEnumerable<string> folders, IReadOnlyCollection<string> active)
    {
        ArgumentException.ThrowIfNullOrEmpty(work);
        ArgumentNullException.ThrowIfNull(folders);
        ArgumentNullException.ThrowIfNull(active);
        var root = Path.Combine(Full(work), Channels.ChannelLimits.Folder);
        var running = active.Select(Full).ToHashSet(Comparer);
        return folders.Select(Full).Where(f => Same(Path.GetDirectoryName(f), root) && Path.GetFileName(f).Length > 0 && !running.Contains(f))
            .Distinct(Comparer).Order(StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// Whether <paramref name="folder"/> is exactly <paramref name="tools"/> + <c>.old-</c>/<c>.failed-</c>/<c>.aside-</c> + a
    /// <c>yyyyMMdd-HHmmss</c> stamp.
    /// </summary>
    public static bool Matches(string tools, string folder)
    {
        ArgumentNullException.ThrowIfNull(tools);
        ArgumentNullException.ThrowIfNull(folder);
        if (tools.Length == 0 || folder.Length <= tools.Length || !folder.StartsWith(tools, Comparison))
        {
            return false;
        }

        var suffix = folder[tools.Length..];
        return Stamped().IsMatch(suffix);
    }

    [GeneratedRegex(@"^\.(old|failed|aside)-[0-9]{8}-[0-9]{6}$", RegexOptions.CultureInvariant)]
    private static partial Regex Stamped();

    private static StringComparison Comparison => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private static StringComparer Comparer => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private static bool Same(string? a, string b) => a is not null && string.Equals(a, b, Comparison);

    private static string Full(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
}
