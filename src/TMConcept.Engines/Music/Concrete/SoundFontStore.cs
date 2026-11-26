using Microsoft.Extensions.Logging;
using TMConcept.Core.Paths;
using TMConcept.Core.Tools;

namespace TMConcept.Engines.Music;

/// <inheritdoc cref="ISoundFontStore" />
/// <remarks>
/// <c>&lt;tools&gt;/soundfonts/&lt;version&gt;/</c>
/// MuseScore_General.sf3 (<see cref="SoundFontPins"/>). The downloader keeps a partial file apart and only names the file when its SHA-256
/// matched, so the file with its pinned size is the finished download.
/// </remarks>
public sealed class SoundFontStore(IAppPaths paths, IArchiveDownloader downloader, ILogger<SoundFontStore> logger) : ISoundFontStore
{
    /// <inheritdoc />
    public string Folder => Path.Combine(paths.ToolsDirectory, MusicSynthRules.SoundFontFolder, SoundFontPins.Version);

    /// <inheritdoc />
    public string File => Path.Combine(Folder, SoundFontPins.FileName);

    /// <inheritdoc />
    public bool Ready
    {
        get
        {
            var file = new FileInfo(File);
            return file.Exists && file.Length == SoundFontPins.Bytes;
        }
    }

    /// <inheritdoc />
    public async Task InstallAsync(IProgress<ToolProgress>? progress, CancellationToken cancellationToken)
    {
        if (Ready)
        {
            return;
        }

        Directory.CreateDirectory(Folder);
        await downloader.DownloadAsync(SoundFontPins.Archive, File, progress, cancellationToken).ConfigureAwait(false);
        await System.IO.File.WriteAllTextAsync(Path.Combine(Folder, SoundFontPins.LicenseFileName), SoundFontPins.LicenseNotice, cancellationToken)
            .ConfigureAwait(false);
        logger.LogInformation("The SoundFont of the composed music is installed (MuseScore General {Version})", SoundFontPins.Version);
    }
}
