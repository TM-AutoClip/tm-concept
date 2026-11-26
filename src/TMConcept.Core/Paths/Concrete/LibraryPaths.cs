using System.Text;

namespace TMConcept.Core.Paths;

/// <summary>
/// Paths inside the library folder (videos and channel photos). Records keep paths relative to the library folder; a stored path can never
/// leave it (no rooted path, no "..").
/// </summary>
public static class LibraryPaths
{
    /// <summary>The buffer of a file copy.</summary>
    private const int CopyBufferBytes = 81920;

    private const int NameMax = 60;

    /// <summary>A folder or file name that is valid on Windows and Linux (invalid characters removed, length limited, never empty).</summary>
    public static string SafeName(string? name, string fallback = "item")
    {
        var builder = new StringBuilder();
        foreach (var c in (name ?? string.Empty).Trim())
        {
            if (char.IsControl(c) || "<>:\"/\\|?*".Contains(c, StringComparison.Ordinal))
            {
                builder.Append(' ');
            }
            else
            {
                builder.Append(c);
            }
        }

        var result = string.Join(' ', builder.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries)).Trim('.', ' ');
        if (result.Length > NameMax)
        {
            result = result[..NameMax].Trim('.', ' ');
        }

        return result.Length == 0 ? fallback : result;
    }

    /// <summary>The full path of a stored relative path; <see cref="InvalidOperationException"/> when it would leave the library folder.</summary>
    public static string Resolve(string libraryDirectory, string relative)
    {
        ArgumentException.ThrowIfNullOrEmpty(libraryDirectory);
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative) || relative.Replace('\\', '/').Split('/').Contains(".."))
        {
            throw new InvalidOperationException("The stored path is not inside the library folder.");
        }

        var root = Path.GetFullPath(libraryDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(Path.Combine(root, relative));
        return full.StartsWith(root, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)
            ? full
            : throw new InvalidOperationException("The stored path is not inside the library folder.");
    }

    /// <summary>A path relative to the library folder with forward slashes.</summary>
    public static string Relative(string libraryDirectory, string fullPath) =>
        Path.GetRelativePath(libraryDirectory, fullPath).Replace('\\', '/');

    /// <summary>Whether <paramref name="path"/> lies strictly inside <paramref name="folder"/> (the system's case rule for paths).</summary>
    public static bool Inside(string path, string folder)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(folder);
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder)) + Path.DirectorySeparatorChar;
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return full.Length > root.Length && full.StartsWith(root, comparison);
    }

    /// <summary>Copies <paramref name="source"/> to <paramref name="target"/> asynchronously (<paramref name="mode"/>: create anew or overwrite).</summary>
    public static async Task CopyAsync(string source, string target, FileMode mode, CancellationToken cancellationToken)
    {
        await using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, CopyBufferBytes, useAsync: true);
        await using var output = new FileStream(target, mode, FileAccess.Write, FileShare.None, CopyBufferBytes, useAsync: true);
        await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
    }
}
