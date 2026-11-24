using Microsoft.Extensions.Logging;
using TMConcept.Core.Paths;
using TMConcept.Core.Tools;

namespace TMConcept.Core.Music;

/// <summary>
/// The ready music library on this computer (0.1.45, C4): <c>&lt;tools&gt;/music-library/&lt;catalog version&gt;/</c> with one file per track
/// of the embedded catalog (<see cref="MusicCatalog"/>) and a <see cref="MusicLibraryRules.Marker"/> written last. The install (only after the
/// user agreed to the download list, <see cref="MusicLibraryComponent"/>) fetches every missing track through the app's downloader from
/// <see cref="MusicLibraryRules.Host"/> and checks it against the SHA-256 of the catalog; a track already there is kept. The tracks the user
/// picks from an Openverse search are kept apart in <c>&lt;data&gt;/music/</c>.
/// </summary>
public sealed class MusicLibraryStore(IAppPaths paths, IArchiveDownloader downloader, ILogger<MusicLibraryStore> logger) : IMusicLibrary
{
    private readonly MusicCatalogFile _catalog = MusicCatalog.Shipped;

    /// <inheritdoc />
    public IReadOnlyList<MusicTrack> Tracks => _catalog.Tracks;

    /// <inheritdoc />
    public string Folder => Path.Combine(paths.ToolsDirectory, MusicLibraryRules.Folder, _catalog.Version);

    /// <inheritdoc />
    public string PickedFolder => Path.Combine(paths.DataDirectory, OpenverseRules.Folder);

    /// <inheritdoc />
    public bool Ready => File.Exists(Path.Combine(Folder, MusicLibraryRules.Marker)) && Missing().Count == 0;

    /// <summary>The tracks whose file is not here (or has another size).</summary>
    public IReadOnlyList<MusicTrack> Missing() => _catalog.Tracks.Where(t => !Present(t)).ToList();

    /// <inheritdoc />
    public string? FileOf(MusicTrack track)
    {
        ArgumentNullException.ThrowIfNull(track);
        var own = _catalog.Tracks.FirstOrDefault(t => t.Id == track.Id);
        return own is not null && Present(own) ? Path.Combine(Folder, own.File) : null;
    }

    /// <summary>The download of a track (pinned by the catalog's SHA-256).</summary>
    public static ToolArchive Archive(MusicTrack track, string version)
    {
        ArgumentNullException.ThrowIfNull(track);
        return new ToolArchive(ToolKind.MusicTrack, version, "any", new Uri(track.DownloadUrl), track.Sha256, string.Empty, track.File, track.Bytes);
    }

    /// <summary>Downloads and checks every missing track, then writes the marker (network; only after the user agreed).</summary>
    public async Task InstallAsync(IProgress<ToolProgress>? progress, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Folder);
        var missing = Missing();
        var total = Math.Max(1, missing.Sum(t => t.Bytes));
        long done = 0;
        foreach (var track in missing)
        {
            var before = done;
            var part = new Progress<ToolProgress>(p => progress?.Report(new ToolProgress((before + (p.Share * track.Bytes)) / total, track.Title)));
            await downloader.DownloadAsync(Archive(track, _catalog.Version), Path.Combine(Folder, track.File), part, cancellationToken).ConfigureAwait(false);
            done += track.Bytes;
            progress?.Report(new ToolProgress(done / (double)total, track.Title));
        }

        await File.WriteAllTextAsync(Path.Combine(Folder, MusicLibraryRules.Marker), _catalog.Version, cancellationToken).ConfigureAwait(false);
        logger.LogInformation("Music library {Version} installed ({Count} tracks, {Downloaded} downloaded)", _catalog.Version, _catalog.Tracks.Count, missing.Count);
    }

    private bool Present(MusicTrack track)
    {
        var file = new FileInfo(Path.Combine(Folder, track.File));
        return file.Exists && file.Length == track.Bytes;
    }
}
