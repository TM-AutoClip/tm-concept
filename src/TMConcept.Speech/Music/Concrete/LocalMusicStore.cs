using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using TMConcept.Core.Tools;

namespace TMConcept.Speech.Music;

/// <summary>
/// The files of the local music families (28 Nov 2026; called by the components only after the user agreed to the download list, rule 18).
/// A model: every pinned file it lacks (a file counts when it is there with its size; the downloader checks the SHA-256 and deletes a broken
/// download), then its marker, which holds a fingerprint of its whole file list (a new pin makes it missing again). Files a model shares with
/// others of its family (MAGNeT's T5) are downloaded once. A family's source (MAGNeT: audiocraft): the archive pinned by its SHA-256, unpacked,
/// its check file looked for, then put in place of the old one, then its marker (the archive's address). Nothing is started here.
/// </summary>
public sealed class LocalMusicStore(LocalMusicLayout layout, IArchiveDownloader downloader, ILogger<LocalMusicStore> logger)
{
    private PinnedFileInstaller Files => new(downloader, logger);

    /// <summary>The fingerprint of a model: its files' paths and SHA-256s.</summary>
    public static string Fingerprint(LocalMusicVariant variant)
    {
        ArgumentNullException.ThrowIfNull(variant);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', variant.Files.Select(f => f.ExecutablePath + " " + f.Sha256)))));
    }

    /// <summary>The files of <paramref name="variant"/> that are not there with their size.</summary>
    public IReadOnlyList<ToolArchive> Missing(LocalMusicFamily family, LocalMusicVariant variant)
    {
        ArgumentNullException.ThrowIfNull(variant);
        return variant.Files.Where(f => !Present(family, f)).ToList();
    }

    /// <summary>Whether every file of <paramref name="variant"/> is there and its marker names this file list (files only).</summary>
    public bool ModelReady(LocalMusicFamily family, LocalMusicVariant variant)
    {
        try
        {
            var marker = layout.ModelMarker(family, variant);
            return File.Exists(marker) && File.ReadAllText(marker).Trim() == Fingerprint(variant) && Missing(family, variant).Count == 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Whether the source <paramref name="family"/> runs from is in place (true for a family without one).</summary>
    public bool SourceReady(LocalMusicFamily family)
    {
        ArgumentNullException.ThrowIfNull(family);
        if (family.Source is not { } source)
        {
            return true;
        }

        try
        {
            var marker = layout.SourceMarker(family);
            return File.Exists(marker) && File.ReadAllText(marker).Trim() == source.Url.ToString()
                && File.Exists(Path.Combine(layout.SourceFolder(family), source.ExecutablePath));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Downloads every missing file of <paramref name="variant"/> (network; only after the user agreed), then writes its marker.</summary>
    public async Task InstallModelAsync(LocalMusicFamily family, LocalMusicVariant variant, IProgress<ToolProgress>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(family);
        ArgumentNullException.ThrowIfNull(variant);
        // Its own folder first: from now on it names its shared files as in use (LocalMusicWeightsComponent.SharedFolders), so a clean-up
        // during the download never takes them.
        Directory.CreateDirectory(layout.ModelFolder(family, variant));
        var downloaded = await Files.DownloadMissingAsync(variant.Files.Select(f => Download(family, f)).ToList(), progress, cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(layout.ModelMarker(family, variant), Fingerprint(variant), cancellationToken).ConfigureAwait(false);
        logger.LogInformation("The local music model {Model} is installed ({Count} files, {Downloaded} downloaded)", variant.Id, variant.Files.Count, downloaded);
    }

    /// <summary>Downloads and unpacks the source <paramref name="family"/> runs from (network; only after the user agreed).</summary>
    public async Task InstallSourceAsync(LocalMusicFamily family, IProgress<ToolProgress>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(family);
        if (family.Source is not { } source)
        {
            return;
        }

        var downloads = layout.Downloads(family);
        progress?.Report(new ToolProgress(0.05, source.Root));
        await Files.UnpackAsync(source, downloads, layout.SourceFolder(family), Replace, cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(layout.SourceMarker(family), source.Url.ToString(), cancellationToken).ConfigureAwait(false);
        Files.TryDelete(downloads);
        progress?.Report(new ToolProgress(1, source.Root));
        logger.LogInformation("The source of {Family} is installed ({Version})", family.Name, source.Version);
    }

    /// <summary>Puts an unpacked source in place of the old one as a whole: a file a newer commit no longer has must not stay importable.</summary>
    private static void Replace(string from, string to)
    {
        if (Directory.Exists(to))
        {
            Directory.Delete(to, recursive: true);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(to)!);
        Directory.Move(from, to);
    }

    private PinnedDownload Download(LocalMusicFamily family, ToolArchive file) => new(file, layout.FileOf(family, file));

    private bool Present(LocalMusicFamily family, ToolArchive file) => PinnedFileInstaller.Present(Download(family, file));
}
