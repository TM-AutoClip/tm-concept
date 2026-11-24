using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using TMConcept.Core.Processes;
using TMConcept.Core.References;
using TMConcept.Core.Tools;
using TMConcept.Engines.Tools;

namespace TMConcept.Engines.References;

/// <summary>
/// <see cref="IReferenceDownloader"/> with yt-dlp (S1, 0.1.52; the app's pinned copy, a component installed only after the user's yes). One
/// call downloads one video (<see cref="Arguments"/>): the user's configuration files, plugins and cache are ignored, no playlist, no cookies
/// from a file or a browser, no sign-in; a format at most <see cref="ReferenceLimits.MaxHeight"/> pixels high, preferring the largest one up to
/// <see cref="ReferenceLimits.PreferredHeight"/> (the lowest that is plenty for the measure), only
/// the first <see cref="ReferenceLimits.MaxSeconds"/> seconds (cut by the app's ffmpeg) and at most <see cref="ReferenceLimits.MaxFileSize"/>.
/// The file goes into the measure's own folder, which the measure deletes. A below-normal process like every child; the processor only.
/// </summary>
public sealed partial class YtDlpDownloader(ToolInstaller tools, IProcessRunner runner, ILogger<YtDlpDownloader> logger) : IReferenceDownloader
{
    /// <inheritdoc />
    public bool Ready => tools.Status(ToolKind.VideoDownloader).Ready;

    /// <summary>
    /// The yt-dlp arguments for one reference (pure; the tests read them). <paramref name="node"/>: the app's Node.js, given to yt-dlp as its
    /// JavaScript runtime (some sites need one to list their formats); empty = none.
    /// </summary>
    public static IReadOnlyList<string> Arguments(Uri address, string folder, string ffmpeg, string node)
    {
        ArgumentNullException.ThrowIfNull(address);
        var c = CultureInfo.InvariantCulture;
        var height = ReferenceLimits.MaxHeight.ToString(c);
        var arguments = new List<string>
        {
            "--ignore-config", "--no-plugin-dirs", "--no-cache-dir",
            "--no-playlist", "--no-cookies", "--no-cookies-from-browser",
            "--no-mtime", "--no-write-info-json", "--no-write-thumbnail", "--no-write-subs",
            "--newline", "--progress", "--no-simulate", "--print", "after_move:filepath",
            "--socket-timeout", ReferenceLimits.SocketTimeoutSeconds.ToString(c), "--retries", "3",
            "--max-filesize", ReferenceLimits.MaxFileSize,
            "-f", $"bv*[height<={height}]+ba/b[height<={height}]/wv*+ba/w",
            "-S", "res:" + ReferenceLimits.PreferredHeight.ToString(c),
            "--merge-output-format", "mkv",
            "--download-sections", "*0-" + ReferenceLimits.MaxSeconds.ToString(c),
            "--ffmpeg-location", ffmpeg,
            "-o", Path.Combine(folder, ReferenceLimits.DownloadName + ".%(ext)s"),
        };
        if (node.Length > 0)
        {
            arguments.AddRange(["--js-runtimes", "node:" + node]);
        }

        // "--" ends the options: an address can never be read as one.
        arguments.AddRange(["--", address.AbsoluteUri]);
        return arguments;
    }

    /// <summary>The share (0–1) of a progress line of yt-dlp ("[download]  42.3% of …"), or null.</summary>
    public static double? Progress(string line)
    {
        var match = ProgressLine().Match(line ?? string.Empty);
        return match.Success && double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var percent)
            ? Math.Clamp(percent / 100, 0, 1)
            : null;
    }

    /// <summary>The downloaded file in <paramref name="folder"/>: the largest finished file named after <see cref="ReferenceLimits.DownloadName"/>, or null.</summary>
    public static string? Downloaded(string folder) =>
        !Directory.Exists(folder)
            ? null
            : Directory.EnumerateFiles(folder, ReferenceLimits.DownloadName + ".*")
                .Where(f => !f.EndsWith(".part", StringComparison.OrdinalIgnoreCase) && !f.EndsWith(".ytdl", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(f => new FileInfo(f).Length).FirstOrDefault();

    /// <inheritdoc />
    public async Task<string> DownloadAsync(Uri address, string folder, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(address);
        if (!Ready)
        {
            throw new ReferenceMeasureException("yt-dlp is not installed.") { Code = "missing" };
        }

        Directory.CreateDirectory(folder);
        var node = tools.Status(ToolKind.Node) is { Ready: true } n ? n.ExecutablePath : string.Empty;
        ProcessResult result;
        try
        {
            result = await runner.RunAsync(new ProcessStart(tools.Require(ToolKind.VideoDownloader),
                Arguments(address, folder, tools.Require(ToolKind.Ffmpeg), node), folder), line =>
            {
                if (Progress(line) is { } share)
                {
                    progress?.Report(share);
                }
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is ToolInstallException or System.ComponentModel.Win32Exception)
        {
            throw new ReferenceMeasureException("yt-dlp could not be started: " + ex.Message, ex) { Code = "download", Detail = ex.Message };
        }

        var file = Downloaded(folder);
        if (result.ExitCode != 0 || file is null)
        {
            var detail = ChildOutput.Summary(result.LastLines.Replace(folder, "<folder>", StringComparison.Ordinal)).Trim();
            logger.LogWarning("yt-dlp could not download the reference from {Host} (exit {Code}): {Error}", address.Host, result.ExitCode, detail);
            throw new ReferenceMeasureException("yt-dlp could not download the reference.") { Code = "download", Detail = detail };
        }

        progress?.Report(1);
        return file;
    }

    [GeneratedRegex(@"^\[download\]\s+(\d+(?:\.\d+)?)%", RegexOptions.CultureInvariant)]
    private static partial Regex ProgressLine();
}
