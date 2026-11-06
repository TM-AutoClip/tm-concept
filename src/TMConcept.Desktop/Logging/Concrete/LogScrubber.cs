using System.Text.RegularExpressions;
using TMConcept.Core.Security;

namespace TMConcept.Desktop.Logging;

/// <summary>
/// The last guard before a line reaches a log file or a report. Paths: the library folder becomes <c>&lt;library&gt;</c> and the two folder
/// names under it (the profile folder and the video folder, named after the profile and the concept title) become <c>&lt;profile&gt;</c> and
/// <c>&lt;folder&gt;</c>; a data folder or documents folder outside the home folder (portable <c>--data-dir</c>, a redirected Documents
/// folder, a network share) becomes <c>&lt;data&gt;</c> / <c>&lt;documents&gt;</c>; the user's home folder becomes <c>~</c>. Every path is
/// matched with either slash and as escaped in JSON. Then known names (<see cref="WithNames"/>: profile names and titles, for a report)
/// become <c>&lt;name&gt;</c>, e-mail addresses become <c>&lt;e-mail&gt;</c>, and tokens and keys are masked by <see cref="Redactor"/>
/// (Bearer / Basic headers, provider keys, secret query values and JSON fields). Code does not log these on purpose; this catches what a
/// tool, a provider or an exception message echoes.
/// </summary>
public sealed partial class LogScrubber
{
    private const string Separator = @"(?:\\\\|[\\/])";
    private const string Segment = @"[^\\/\r\n""'<>|?*:]+";
    private readonly Regex? _library;
    private readonly (string Path, string Tag)[] _folders;
    private readonly Regex? _names;

    /// <summary>
    /// Masks <paramref name="home"/> and, when given, the <paramref name="library"/> folder, and the <paramref name="data"/> and
    /// <paramref name="documents"/> folders where they are outside the home folder (empty or a drive root: that folder is not masked).
    /// </summary>
    public LogScrubber(string home, string? library = null, string? data = null, string? documents = null)
    {
        var homeRoot = Root(home);
        var folders = new List<(string, string)>();
        foreach (var (folder, tag) in new[] { (data, "<data>"), (documents, "<documents>") })
        {
            var root = Root(folder);
            if (root.Length > 0 && !Inside(root, homeRoot))
            {
                folders.AddRange(Variants(root).Select(v => (v, tag)));
            }
        }

        folders.AddRange(Variants(homeRoot).Select(v => (v, "~")));
        _folders = folders.OrderByDescending(f => f.Item1.Length).ToArray();
        var libraryRoot = Root(library);
        if (libraryRoot.Length > 0)
        {
            var roots = string.Join('|', Variants(libraryRoot).OrderByDescending(v => v.Length).Select(Regex.Escape));
            _library = new Regex($@"(?:{roots})(?:(?<s1>{Separator})(?<p>{Segment}))?(?:(?<s2>{Separator})(?<f>{Segment}))?(?=[\\/\r\n""'<>|?*:\s,;)\]]|$)",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }
    }

    private LogScrubber(LogScrubber source, Regex? names)
    {
        _library = source._library;
        _folders = source._folders;
        _names = names;
    }

    /// <summary>The scrubber of the signed-in user (home folder only).</summary>
    public static LogScrubber ForCurrentUser { get; } = new(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));

    /// <summary>The scrubber of the app with <paramref name="paths"/>: home, library, data and documents folders.</summary>
    public static LogScrubber For(Paths.AppPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        return new(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), paths.LibraryDirectory, paths.DataDirectory, paths.DocumentsDirectory);
    }

    /// <summary>The speech worker's scrubber: it knows only its log folder, inside the data folder.</summary>
    public static LogScrubber ForLogFolder(string logDirectory) =>
        new(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), data: Path.GetDirectoryName(Path.GetFullPath(logDirectory)),
            documents: Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));

    /// <summary>
    /// The same scrubber that also masks <paramref name="names"/> (profile names, concept and video titles) as <c>&lt;name&gt;</c>, as whole
    /// words, ignoring case; names shorter than three characters are left (they would mask ordinary words).
    /// </summary>
    public LogScrubber WithNames(IEnumerable<string> names)
    {
        ArgumentNullException.ThrowIfNull(names);
        var list = names.Select(n => n.Trim()).Where(n => n.Length >= 3).Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(n => n.Length).Select(Regex.Escape).ToList();
        return list.Count == 0
            ? new LogScrubber(this, null)
            : new LogScrubber(this, new Regex(@"(?<![\p{L}\p{N}])(?:" + string.Join('|', list) + @")(?![\p{L}\p{N}])",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant));
    }

    /// <summary>The text with folders, known names, e-mail addresses and secrets masked.</summary>
    public string Scrub(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var result = text;
        if (_library is not null)
        {
            result = _library.Replace(result, m => "<library>"
                + (m.Groups["p"].Success ? m.Groups["s1"].Value + "<profile>" : string.Empty)
                + (m.Groups["f"].Success ? m.Groups["s2"].Value + "<folder>" : string.Empty));
        }

        foreach (var (path, tag) in _folders)
        {
            result = result.Replace(path, tag, StringComparison.OrdinalIgnoreCase);
        }

        if (_names is not null)
        {
            result = _names.Replace(result, "<name>");
        }

        result = Email().Replace(result, "<e-mail>");
        return Redactor.Redact(result);
    }

    /// <summary>The folder without a trailing slash; empty for nothing or a drive root (masking it would mask every path).</summary>
    private static string Root(string? folder)
    {
        var trimmed = (folder ?? string.Empty).Trim().TrimEnd('\\', '/');
        return trimmed.Length > 3 ? trimmed : string.Empty;
    }

    private static bool Inside(string folder, string home) =>
        home.Length > 0 && (folder.Replace('\\', '/') + "/").StartsWith(home.Replace('\\', '/') + "/", StringComparison.OrdinalIgnoreCase);

    /// <summary>The ways a folder is written: as is, with either slash, and with backslashes escaped as in JSON.</summary>
    private static IEnumerable<string> Variants(string root) => root.Length == 0
        ? []
        : new[] { root, root.Replace('\\', '/'), root.Replace('/', '\\'), root.Replace('/', '\\').Replace("\\", "\\\\", StringComparison.Ordinal) }
            .Distinct(StringComparer.Ordinal);

    [GeneratedRegex(@"(?<![A-Za-z0-9._%+-])[A-Za-z0-9._%+-]+@[A-Za-z0-9-]+(?:\.[A-Za-z0-9-]+)*\.[A-Za-z]{2,}(?![A-Za-z0-9-])")]
    private static partial Regex Email();
}
