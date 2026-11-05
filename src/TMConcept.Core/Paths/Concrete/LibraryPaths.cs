using System.Text;

namespace TMConcept.Core.Paths;

/// <summary>
/// Paths inside the library folder (videos and sample images). Records keep paths relative to the library folder; a stored path can never
/// leave it (no rooted path, no "..").
/// </summary>
public static class LibraryPaths
{
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
}
