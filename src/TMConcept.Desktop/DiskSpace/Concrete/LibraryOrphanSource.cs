using System.Text.RegularExpressions;
using TMConcept.Core.DiskSpace;
using TMConcept.Core.Paths;
using TMConcept.Core.Profiles;
using TMConcept.Core.Videos;
using TMConcept.Desktop.Localization;
using TMConcept.Desktop.Paths;
using TMConcept.Desktop.Startup;

namespace TMConcept.Desktop.DiskSpace;

/// <summary>
/// Files in the video library no record points at (0.1.28), as two storage rows that go only when ticked (<see cref="LibraryOrphanItem"/>):
/// <list type="bullet">
/// <item>video folders without a record: a version folder (<c>&lt;library&gt;/&lt;profile&gt;/&lt;concept&gt;/v&lt;N&gt;</c>) that no video
/// record points at and that holds ONLY the files the app writes there (video, thumbnail, subtitles, narration, the edited narration,
/// <c>source/</c>, <c>audio/</c>): a delete before 0.1.28 left them when "also the files" was not ticked;</item>
/// <item>images no profile uses: a file in a profile's <c>images/</c> or <c>channel/</c> folder whose name is one the app gives a copy and
/// that no sample image record and no channel photo points at.</item>
/// </list>
/// Only the app's own layout is looked at: a loose file, a folder of another name, a folder that also holds anything else (the user put it
/// there) is never listed, and nothing outside the library folder is. Nothing is listed while work runs (a video writes its version folder
/// before its record exists).
/// </summary>
public sealed partial class LibraryOrphanSource(AppPaths paths, IVideoStore videos, IProfileStore profiles, AppSession session, Localizer text)
    : IDiskItemSource
{
    /// <summary>The key of the video folders' row.</summary>
    public const string VideosKey = "library:orphan-videos";

    /// <summary>The key of the images' row.</summary>
    public const string ImagesKey = "library:orphan-images";

    /// <summary>The files and folders the app writes into a video's folder (<see cref="VideoFolders"/>).</summary>
    public static readonly IReadOnlySet<string> OwnNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        VideoFolders.VideoFile, VideoFolders.ThumbnailFile, VideoFolders.SubtitlesFile, VideoFolders.NarrationFile, VideoFolders.ScriptFile,
        VideoFolders.Source, VideoFolders.Audio,
    };

    /// <inheritdoc />
    public async Task<IReadOnlyList<IDiskItem>> ListAsync(UsageMap usage, CancellationToken cancellationToken)
    {
        if (session.WorkRunning)
        {
            return [];
        }

        var (folders, files) = await ScanAsync(cancellationToken).ConfigureAwait(false);
        var rows = new List<IDiskItem>();
        if (folders.Count > 0)
        {
            rows.Add(new LibraryOrphanItem(VideosKey, text.Format("storage.item.library_orphan_videos", folders.Count), DiskSizes.Of(folders), folders,
                OrphansAsync));
        }

        if (files.Count > 0)
        {
            rows.Add(new LibraryOrphanItem(ImagesKey, text.Format("storage.item.library_orphan_images", files.Count), DiskSizes.Of(files), files, OrphansAsync));
        }

        return rows;
    }

    /// <summary>The full paths that are orphans now (asked again right before a removal).</summary>
    public async Task<IReadOnlySet<string>> OrphansAsync(CancellationToken cancellationToken)
    {
        var (folders, files) = await ScanAsync(cancellationToken).ConfigureAwait(false);
        return new HashSet<string>(folders.Concat(files), Comparer);
    }

    /// <summary>
    /// The orphans of <paramref name="library"/>: video folders no one of <paramref name="videoFolders"/> (relative, as stored) points at,
    /// and image copies none of <paramref name="files"/> (relative, as stored) is.
    /// </summary>
    public static (IReadOnlyList<string> Folders, IReadOnlyList<string> Files) Scan(string library, IEnumerable<string?> videoFolders, IEnumerable<string?> files)
    {
        ArgumentException.ThrowIfNullOrEmpty(library);
        ArgumentNullException.ThrowIfNull(videoFolders);
        ArgumentNullException.ThrowIfNull(files);
        var root = Full(library);
        var records = Resolve(root, videoFolders);
        var used = Resolve(root, files);
        var orphanFolders = new List<string>();
        var orphanFiles = new List<string>();
        if (!Directory.Exists(root))
        {
            return (orphanFolders, orphanFiles);
        }

        foreach (var profile in Directory.EnumerateDirectories(root).Order(StringComparer.Ordinal))
        {
            try
            {
                ScanProfile(profile, records, used, orphanFolders, orphanFiles);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A folder that cannot be read is not listed (nothing in it is offered for removal).
            }
        }

        return (orphanFolders, orphanFiles);
    }

    private static void ScanProfile(string profile, HashSet<string> records, HashSet<string> used, List<string> orphanFolders, List<string> orphanFiles)
    {
        foreach (var child in Directory.EnumerateDirectories(profile).Select(Full).Order(StringComparer.Ordinal))
        {
            var name = Path.GetFileName(child);
            if (name is "images" or "channel")
            {
                orphanFiles.AddRange(Directory.EnumerateFiles(child).Select(Full).Where(f => CopyName().IsMatch(Path.GetFileName(f)) && !used.Contains(f)));
                continue;
            }

            var versions = Directory.EnumerateDirectories(child).Select(Full).Where(d => VersionFolders.NumberOf(Path.GetFileName(d)) is not null).ToList();
            orphanFolders.AddRange(versions.Where(v => Unreferenced(v, records) && OnlyOwn(v)).Order(StringComparer.Ordinal));
        }
    }

    private static StringComparer Comparer => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private async Task<(IReadOnlyList<string> Folders, IReadOnlyList<string> Files)> ScanAsync(CancellationToken cancellationToken)
    {
        var records = await videos.AllAsync(cancellationToken).ConfigureAwait(false);
        var images = await profiles.AllImagesAsync(cancellationToken).ConfigureAwait(false);
        var photos = (await profiles.ListAsync(cancellationToken).ConfigureAwait(false)).Select(p => p.ChannelPhoto);
        return await Task.Run(() => Scan(paths.LibraryDirectory, records.Select(r => r.Folder), images.Select(i => i.FileName).Concat(photos)), cancellationToken)
            .ConfigureAwait(false);
    }

    private static HashSet<string> Resolve(string root, IEnumerable<string?> relatives)
    {
        var set = new HashSet<string>(Comparer);
        foreach (var relative in relatives.Where(r => !string.IsNullOrWhiteSpace(r)))
        {
            try
            {
                set.Add(Full(LibraryPaths.Resolve(root, relative!)));
            }
            catch (InvalidOperationException)
            {
                // A stored path outside the library points at nothing here.
            }
        }

        return set;
    }

    /// <summary>Whether no record's folder is <paramref name="folder"/> or lies inside it.</summary>
    private static bool Unreferenced(string folder, HashSet<string> records) =>
        !records.Contains(folder) && !records.Any(r => FolderDiscardBase.Inside(r, folder));

    /// <summary>Whether <paramref name="folder"/> holds something and only the names the app writes into a video's folder.</summary>
    private static bool OnlyOwn(string folder)
    {
        var entries = Directory.EnumerateFileSystemEntries(folder).Select(Path.GetFileName).ToList();
        return entries.Count > 0 && entries.All(e => e is not null && OwnNames.Contains(e));
    }

    private static string Full(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    /// <summary>A sample image copy (<c>&lt;10 hex&gt;-name</c>) or a channel photo copy (<c>&lt;32 hex&gt;-&lt;10 hex&gt;.ext</c>).</summary>
    [GeneratedRegex(@"^(?:[0-9a-f]{10}-.+|[0-9a-f]{32}-[0-9a-f]{10}\.[a-z0-9]+)$")]
    private static partial Regex CopyName();
}
