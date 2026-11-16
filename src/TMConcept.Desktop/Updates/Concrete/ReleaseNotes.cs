using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace TMConcept.Desktop.Updates;

/// <summary>
/// Reads a release: the SHA-256 of a file from the notes' SHA-256 block (the lines <c>tools/package.py</c> prints:
/// <c>&lt;64 hex&gt;  TMConcept-0.1.18-win-x64.exe  (60.1 MiB)</c>; also <c>file: hash</c> or a table row) and the file for this computer.
/// A file without exactly one SHA-256 in the notes is never installed.
/// </summary>
public static partial class ReleaseNotes
{
    /// <summary>
    /// The SHA-256 (lower-case hex) the notes give for <paramref name="file"/>: from lines that name the file and hold one 64-digit hex value.
    /// Null when there is none, when two lines give different values, or when the notes have no SHA-256 heading or mention at all.
    /// </summary>
    public static string? Sha256For(string? notes, string file)
    {
        ArgumentException.ThrowIfNullOrEmpty(file);
        if (string.IsNullOrEmpty(notes) || !Mention().IsMatch(notes))
        {
            return null;
        }

        string? found = null;
        foreach (var line in notes.Split('\n'))
        {
            if (!NamesFile(line, file))
            {
                continue;
            }

            var hashes = Hex().Matches(line).Select(m => m.Value.ToLowerInvariant()).Distinct(StringComparer.Ordinal).ToList();
            if (hashes.Count != 1)
            {
                continue;
            }

            if (found is not null && found != hashes[0])
            {
                return null;
            }

            found = hashes[0];
        }

        return found;
    }

    /// <summary>The file of <paramref name="release"/> for <paramref name="rid"/> (win-x64 → <c>…-win-x64.exe</c>, linux-x64/arm64 → <c>…-linux-x64.tar.gz</c>).</summary>
    public static GitHubAsset? AssetFor(GitHubRelease release, string rid)
    {
        ArgumentNullException.ThrowIfNull(release);
        var suffix = rid.StartsWith("win-", StringComparison.Ordinal) ? "-" + rid + ".exe" : "-" + rid + ".tar.gz";
        return release.Assets.FirstOrDefault(a => a.Name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>This computer's runtime id among the published ones (win-x64, linux-x64, linux-arm64); empty when there is no build for it.</summary>
    public static string CurrentRid()
    {
        var architecture = RuntimeInformation.ProcessArchitecture;
        if (OperatingSystem.IsWindows() && architecture == Architecture.X64)
        {
            return "win-x64";
        }

        if (OperatingSystem.IsLinux())
        {
            return architecture switch
            {
                Architecture.X64 => "linux-x64",
                Architecture.Arm64 => "linux-arm64",
                _ => string.Empty,
            };
        }

        return string.Empty;
    }

    /// <summary>Whether <paramref name="line"/> names exactly <paramref name="file"/> (not a longer name that contains it).</summary>
    private static bool NamesFile(string line, string file)
    {
        var at = line.IndexOf(file, StringComparison.OrdinalIgnoreCase);
        while (at >= 0)
        {
            var before = at == 0 ? ' ' : line[at - 1];
            var end = at + file.Length;
            var after = end >= line.Length ? ' ' : line[end];
            if (!IsNamePart(before) && !IsNamePart(after))
            {
                return true;
            }

            at = line.IndexOf(file, at + 1, StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    private static bool IsNamePart(char c) => char.IsLetterOrDigit(c) || c is '-' or '_' or '.';

    [GeneratedRegex(@"(?<![0-9A-Fa-f])[0-9A-Fa-f]{64}(?![0-9A-Fa-f])")]
    private static partial Regex Hex();

    [GeneratedRegex(@"SHA-?256", RegexOptions.IgnoreCase)]
    private static partial Regex Mention();
}
