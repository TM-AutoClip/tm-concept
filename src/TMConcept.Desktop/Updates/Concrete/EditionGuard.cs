namespace TMConcept.Desktop.Updates;

/// <summary>
/// Keeps the user's data out of the installed edition's folder: an uninstall empties the install folder and a repair install renames it, so
/// a data folder that is the install folder, lies inside it or holds it (Setup started with <c>--installto</c> pointing at the data folder)
/// would be lost. The installed app refuses to start in that case (<c>Program.Main</c>).
/// </summary>
public static class EditionGuard
{
    /// <summary>Whether <paramref name="dataDirectory"/> and <paramref name="installRoot"/> are the same folder or one lies inside the other.</summary>
    public static bool Overlaps(string dataDirectory, string? installRoot)
    {
        ArgumentException.ThrowIfNullOrEmpty(dataDirectory);
        if (string.IsNullOrEmpty(installRoot))
        {
            return false;
        }

        var data = Normal(dataDirectory);
        var root = Normal(installRoot);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return data.StartsWith(root, comparison) || root.StartsWith(data, comparison);
    }

    private static string Normal(string folder) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder)) + Path.DirectorySeparatorChar;
}
