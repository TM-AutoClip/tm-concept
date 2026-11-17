using System.Text;
using System.Text.RegularExpressions;

namespace TMConcept.Desktop.Updates;

/// <summary>
/// The release notes built into the app (0.1.23): every <c>docs/release-notes/v*.md</c> is embedded at build time, so the "What's new" window
/// works offline. A version's notes hold a <c>## English</c> and a <c>## Türkçe</c> section (<see cref="ReleaseNotesText"/> picks one); the
/// oldest version has two files instead (<c>v0.1.0.md</c> and <c>v0.1.0.tr.md</c>), which are joined into those two sections here.
/// </summary>
public static partial class BundledReleaseNotes
{
    private const string Prefix = "TMConcept.ReleaseNotes.";

    /// <summary>Every embedded version with its notes, newest first.</summary>
    public static IReadOnlyList<VersionNotes> All { get; } = Load();

    /// <summary>The embedded resource names (tests).</summary>
    internal static IReadOnlyList<string> ResourceNames => typeof(BundledReleaseNotes).Assembly.GetManifestResourceNames()
        .Where(n => n.StartsWith(Prefix, StringComparison.Ordinal)).Order(StringComparer.Ordinal).ToList();

    /// <summary>The notes of <paramref name="version"/>; null when none is embedded.</summary>
    public static VersionNotes? Of(AppVersion version) => All.FirstOrDefault(n => n.Version.CompareTo(version) == 0);

    private static List<VersionNotes> Load()
    {
        var english = new Dictionary<AppVersion, string>();
        var turkish = new Dictionary<AppVersion, string>();
        foreach (var name in ResourceNames)
        {
            var match = FileName().Match(name[Prefix.Length..]);
            if (!match.Success || !AppVersion.TryParse(match.Groups[1].Value, out var version))
            {
                continue;
            }

            var body = Read(name);
            if (match.Groups[2].Success)
            {
                turkish[version] = body;
            }
            else
            {
                english[version] = body;
            }
        }

        return english.Select(e => new VersionNotes(e.Key, turkish.TryGetValue(e.Key, out var tr) ? Joined(e.Value, tr) : e.Value, null))
            .OrderByDescending(n => n.Version)
            .ToList();
    }

    /// <summary>Two single-language files as one text with an English and a Turkish section (their own headings two levels lower).</summary>
    private static string Joined(string english, string turkish) =>
        "## English\n\n" + Lower(english) + "\n\n## Türkçe\n\n" + Lower(turkish);

    private static string Lower(string markdown) =>
        string.Join('\n', markdown.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n')
            .Select(line => Heading().IsMatch(line) ? new string('#', Math.Min(6, line.TakeWhile(c => c == '#').Count() + 2)) + line.TrimStart('#') : line));

    private static string Read(string name)
    {
        using var stream = typeof(BundledReleaseNotes).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Release notes '{name}' are not embedded.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    [GeneratedRegex(@"^v(\d+\.\d+\.\d+(?:-[0-9A-Za-z.]+?)?)(\.tr)?\.md$")]
    private static partial Regex FileName();

    [GeneratedRegex(@"^#{1,6}\s")]
    private static partial Regex Heading();
}
