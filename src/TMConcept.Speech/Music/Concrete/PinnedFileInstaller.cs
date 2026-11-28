using System.IO.Compression;
using Microsoft.Extensions.Logging;
using TMConcept.Core.Tools;

namespace TMConcept.Speech.Music;

/// <summary>
/// The download steps the local music models' installs share (28 Nov 2026: ACE-Step 1.5 through rakkase, <see cref="RakkaseInstaller"/>, and
/// the other families, <see cref="LocalMusicStore"/>; called only after the user agreed to the download list, rule 18): pinned files (a file
/// counts when it is there with its size; the downloader checks the SHA-256 and deletes a broken download) and source archives (pinned by
/// SHA-256, unpacked, their check file looked for, then put in place by the caller).
/// </summary>
public sealed class PinnedFileInstaller(IArchiveDownloader downloader, ILogger logger)
{
    /// <summary>Whether <paramref name="download"/> is there with its size.</summary>
    public static bool Present(PinnedDownload download)
    {
        ArgumentNullException.ThrowIfNull(download);
        var file = new FileInfo(download.Target);
        return file.Exists && file.Length == download.File.ApproximateBytes;
    }

    /// <summary>
    /// Downloads every file of <paramref name="downloads"/> that is not there with its size (network); <paramref name="progress"/> gets the share
    /// of the bytes. Returns how many were downloaded.
    /// </summary>
    public async Task<int> DownloadMissingAsync(IReadOnlyList<PinnedDownload> downloads, IProgress<ToolProgress>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(downloads);
        var missing = downloads.Where(d => !Present(d)).ToList();
        var total = Math.Max(1, missing.Sum(d => d.File.ApproximateBytes));
        long done = 0;
        foreach (var download in missing)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(download.Target)!);
            var before = done;
            var bytes = download.File.ApproximateBytes;
            var part = new Progress<ToolProgress>(p => progress?.Report(new ToolProgress((before + (p.Share * bytes)) / total, download.File.ExecutablePath)));
            await downloader.DownloadAsync(download.File, download.Target, part, cancellationToken).ConfigureAwait(false);
            done += bytes;
        }

        return missing.Count;
    }

    /// <summary>
    /// Downloads the source archive <paramref name="archive"/> into <paramref name="downloads"/> (network), unpacks it there, checks that its
    /// root folder holds its check file (<see cref="ToolArchive.ExecutablePath"/>; otherwise <see cref="ToolInstallException"/> "archive"), hands
    /// that root folder to <paramref name="place"/> (which copies or moves it to <paramref name="target"/>) and removes what it unpacked.
    /// </summary>
    public async Task UnpackAsync(ToolArchive archive, string downloads, string target, Action<string, string> place, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(archive);
        ArgumentException.ThrowIfNullOrEmpty(downloads);
        ArgumentException.ThrowIfNullOrEmpty(target);
        ArgumentNullException.ThrowIfNull(place);
        Directory.CreateDirectory(downloads);
        var zip = Path.Combine(downloads, Path.GetFileName(archive.Root) + ".zip");
        await downloader.DownloadAsync(archive, zip, null, cancellationToken).ConfigureAwait(false);
        var staging = Path.Combine(downloads, "unpacked-" + Path.GetFileName(target));
        TryDelete(staging);
        await Task.Run(() =>
        {
            ZipFile.ExtractToDirectory(zip, staging, overwriteFiles: true);
            var top = Path.Combine(staging, archive.Root);
            if (!File.Exists(Path.Combine(top, archive.ExecutablePath)))
            {
                throw new ToolInstallException($"The archive of {archive.Root} has no {archive.ExecutablePath}.") { Code = "archive" };
            }

            place(top, target);
        }, cancellationToken).ConfigureAwait(false);
        TryDelete(staging);
    }

    /// <summary>Removes a temporary folder of an install; a folder that cannot be removed is only logged.</summary>
    public void TryDelete(string folder)
    {
        try
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "A temporary folder of a local music model's install could not be removed");
        }
    }
}
