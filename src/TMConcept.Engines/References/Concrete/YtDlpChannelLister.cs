using System.Globalization;
using Microsoft.Extensions.Logging;
using TMConcept.Core.Channels;
using TMConcept.Core.Processes;
using TMConcept.Core.References;
using TMConcept.Core.Tools;
using TMConcept.Engines.Tools;

namespace TMConcept.Engines.References;

/// <summary>
/// <see cref="IChannelLister"/> with yt-dlp ("Kanaldan esinlen", 0.1.53; the reference video's pinned copy, a component installed only after the
/// user's yes). One call reads a channel's public details as one JSON line (<see cref="Arguments"/>): <c>-J --flat-playlist</c> (nothing is
/// downloaded, every video only listed), the newest <c>count</c> items, the user's configuration files, plugins and cache ignored, no cookies
/// from a file or a browser, no sign-in, the address after "--". The answer is read by <see cref="ChannelListingReader"/>. Neither the address
/// nor the answer is logged (only the host and the counts). A below-normal process like every child; the processor only.
/// </summary>
public sealed class YtDlpChannelLister(ToolInstaller tools, IProcessRunner runner, ILogger<YtDlpChannelLister> logger) : IChannelLister
{
    /// <inheritdoc />
    public bool Ready => tools.Status(ToolKind.VideoDownloader).Ready;

    /// <summary>
    /// The yt-dlp arguments of one channel (pure; the tests read them). <paramref name="node"/>: the app's Node.js as yt-dlp's JavaScript
    /// runtime; empty = none.
    /// </summary>
    public static IReadOnlyList<string> Arguments(Uri address, int count, string node)
    {
        ArgumentNullException.ThrowIfNull(address);
        var c = CultureInfo.InvariantCulture;
        var arguments = new List<string>
        {
            "--ignore-config", "--no-plugin-dirs", "--no-cache-dir",
            "--no-cookies", "--no-cookies-from-browser",
            "--flat-playlist", "--dump-single-json",
            "--playlist-items", "1:" + Math.Max(1, count).ToString(c),
            "--socket-timeout", ReferenceLimits.SocketTimeoutSeconds.ToString(c), "--retries", "3",
        };
        if (node.Length > 0)
        {
            arguments.AddRange(["--js-runtimes", "node:" + node]);
        }

        // "--" ends the options: an address can never be read as one.
        arguments.AddRange(["--", address.AbsoluteUri]);
        return arguments;
    }

    /// <inheritdoc />
    public async Task<ChannelListing> ListAsync(Uri address, int count, string folder, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(address);
        if (!Ready)
        {
            throw new ChannelInspireException("yt-dlp is not installed.") { Code = "missing" };
        }

        Directory.CreateDirectory(folder);
        var node = tools.Status(ToolKind.Node) is { Ready: true } n ? n.ExecutablePath : string.Empty;
        string? json = null;
        ProcessResult result;
        try
        {
            result = await runner.RunAsync(new ProcessStart(tools.Require(ToolKind.VideoDownloader), Arguments(address, count, node), folder), line =>
            {
                if (json is null && line.TrimStart().StartsWith('{'))
                {
                    json = line;
                }
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is ToolInstallException or System.ComponentModel.Win32Exception)
        {
            throw new ChannelInspireException("yt-dlp could not be started: " + ex.Message, ex) { Code = "list", Detail = ex.Message };
        }

        // A JSON line is the answer even when one listed item failed (yt-dlp may then end with an error code).
        if (json is null)
        {
            var detail = ChildOutput.Summary(string.Join('\n', result.LastLines.Split('\n').Where(l => !l.TrimStart().StartsWith('{')))
                .Replace(folder, "<folder>", StringComparison.Ordinal)).Trim();
            logger.LogWarning("yt-dlp could not read the channel on {Host} (exit {Code})", address.Host, result.ExitCode);
            throw new ChannelInspireException("yt-dlp could not read the channel.") { Code = "list", Detail = detail };
        }

        var listing = ChannelListingReader.Read(json, count);
        logger.LogInformation("yt-dlp read the channel on {Host}: {Videos} videos, {Tabs} tabs", address.Host, listing.Videos.Count, listing.Tabs.Count);
        return listing;
    }
}
