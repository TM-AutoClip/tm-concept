using TMConcept.Core.AppOllama;
using TMConcept.Core.Components;
using TMConcept.Core.DiskSpace;
using TMConcept.Core.Paths;
using TMConcept.Desktop.Localization;
using TMConcept.Desktop.Paths;
using TMConcept.Speech.Python;

namespace TMConcept.Desktop.DiskSpace;

/// <summary>
/// The space no component owns, as storage rows (only what is safe to lose: made again when needed, or never needed again):
/// <list type="bullet">
/// <item>unfinished downloads (<c>tools/downloads</c>) and the Python voices' helper folders (<c>speech-py/scripts</c>, <c>locks</c>,
/// <c>home</c>: written again at the next install or narration);</item>
/// <item>the files of the app's own Ollama models folder that no model uses (<see cref="AppModelStore.UnreferencedBlobs"/>: the received
/// part of a cancelled pull, layers of a removed model);</item>
/// <item>folders inside <c>tools/</c> that no component owns (an older pinned version, a build for another graphics setting); image model
/// folders (<c>tools/images/&lt;id&gt;</c>) this copy does not know are a row of their own that only the full delete takes;</item>
/// <item>old components folders next to <c>tools/</c> left by "Reinstall from scratch" of a version up to 0.1.56 (<c>tools.old-*</c>,
/// <c>tools.failed-*</c>, <c>tools.aside-*</c>; the start-up sweep deletes them too);</item>
/// <item>the work caches: voice sample files, the listen and sample-render files, the scene images of concepts that no longer exist, and
/// cached narration, run folders and scene images older than the work keep time (the clean-up takes those; the recent ones that a retry
/// still uses go only with the full delete: a retry of a failed video would otherwise pay the online voice and the online images again).</item>
/// </list>
/// The database, keys, settings, backups and logs are never rows here; library files no record points at are <see cref="LibraryOrphanSource"/>'s.
/// </summary>
public sealed class LeftoverDiskSource(AppPaths paths, PythonLayout python, ComponentPlanner planner, AppModelStore appModels, Localizer text, TimeProvider clock,
    Core.Concepts.IConceptStore concepts) : IDiskItemSource
{
    /// <summary>The names an old components folder starts with (next to <c>tools</c>).</summary>
    public static readonly IReadOnlyList<string> AsidePrefixes =
        [LeftoverSweep.OldInfix, LeftoverSweep.FailedInfix, LeftoverSweep.AsideInfix];

    /// <inheritdoc />
    public async Task<IReadOnlyList<IDiskItem>> ListAsync(UsageMap usage, CancellationToken cancellationToken)
    {
        // The scene images of a concept that no longer exists (deleted for good, its profile deleted) are of no use: no 7-day wait for them.
        var gone = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var images = Path.Combine(paths.WorkDirectory, "scene-images");
        if (Directory.Exists(images))
        {
            foreach (var folder in Directory.EnumerateDirectories(images))
            {
                if (!Guid.TryParseExact(Path.GetFileName(folder), "N", out var id) || await concepts.GetAsync(id, cancellationToken).ConfigureAwait(false) is null)
                {
                    gone.Add(Path.GetFileName(folder));
                }
            }
        }

        return await Task.Run<IReadOnlyList<IDiskItem>>(() => List(gone).Where(i => i.Bytes > 0).ToList(), cancellationToken).ConfigureAwait(false);
    }

    private IEnumerable<IDiskItem> List(HashSet<string> goneConcepts)
    {
        var tools = paths.ToolsDirectory;
        var downloads = Path.Combine(tools, ComponentFolders.DownloadsFolder);
        yield return Folder("tools:downloads", "storage.item.downloads", DiskArea.Tools, [downloads], true);

        string[] helpers = [python.ScriptsFolder, python.LocksFolder, python.HomeFolder];
        yield return Folder("tools:speech-py-helpers", "storage.item.python_helpers", DiskArea.Tools, helpers, true);

        yield return Folder(DiskSelection.AppModelLeftoversKey, "storage.item.app_model_leftovers", DiskArea.Models, appModels.UnreferencedBlobs(), true);

        // A folder several components share (a local music family's shared model files) is owned while one of them names it.
        var owned = planner.All.SelectMany(c => c.Folders().Concat(c.SharedFolders())).Concat(helpers).Append(downloads).Select(Full).ToList();
        var unowned = Unowned(tools, owned).ToList();
        // An image model folder this process has no catalog entry for is not a leftover: a newer version or a remote catalog entry this copy
        // refused (newer minApp, newer stable-diffusion.cpp, a damaged kept copy) installed it, and it is gigabytes to download again. It is
        // listed on its own and goes only with the full delete, never with the one-click clean-up.
        var imageRoot = Full(Path.Combine(tools, "images"));
        var unknownImages = unowned.Where(f => Same(f, imageRoot) || LibraryPaths.Inside(f, imageRoot)).ToList();
        yield return Folder("tools:unowned", "storage.item.unowned", DiskArea.Tools, unowned.Except(unknownImages).ToList(), true);
        yield return Folder("tools:unknown-image-models", "storage.item.unknown_image_models", DiskArea.Models, unknownImages, false);

        var parent = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(tools));
        var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(tools));
        if (parent is not null && Directory.Exists(parent))
        {
            foreach (var aside in Directory.EnumerateDirectories(parent, name + ".*").Order(StringComparer.Ordinal))
            {
                var suffix = Path.GetFileName(aside)[name.Length..];
                if (AsidePrefixes.Any(p => suffix.StartsWith(p, StringComparison.Ordinal)))
                {
                    yield return new FolderDiskItem("tools:aside:" + Path.GetFileName(aside), text.Format("storage.item.aside", Path.GetFileName(aside)),
                        DiskArea.Tools, DiskSizes.Of(aside), [aside], true, true);
                }
            }
        }

        var work = paths.WorkDirectory;
        // Narration is one file per spoken text (a cache hit costs nothing, a miss may pay an online voice again).
        var speech = Path.Combine(work, "speech-cache");
        var clips = Directory.Exists(speech) ? Directory.EnumerateFiles(speech).ToList() : [];
        var oldClips = clips.Where(c => clock.GetUtcNow() - File.GetLastWriteTimeUtc(c) > Core.Videos.VideoPipeline.WorkKeep).ToList();
        yield return Folder("work:speech-cache:old", "storage.item.speech_cache.old", DiskArea.Work, oldClips, true);
        yield return Folder("work:speech-cache:recent", "storage.item.speech_cache.recent", DiskArea.Work, clips.Except(oldClips).ToList(), false);
        yield return Folder("work:voices", "storage.item.voice_files", DiskArea.Work, [Path.Combine(work, "voices")], true);
        yield return Folder("work:listen", "storage.item.listen", DiskArea.Work, [Path.Combine(work, "listen")], true);
        yield return Folder("work:sample", "storage.item.sample_render", DiskArea.Work, [Path.Combine(work, "sample")], true);
        var sceneImages = Path.Combine(work, "scene-images");
        var orphanImages = Directory.Exists(sceneImages)
            ? Directory.EnumerateDirectories(sceneImages).Where(d => goneConcepts.Contains(Path.GetFileName(d))).ToList()
            : [];
        yield return Folder("work:scene-images:orphan", "storage.item.scene_images.orphan", DiskArea.Work, orphanImages, true);
        foreach (var (kind, oldKey, recentKey) in new[]
        {
            ("videos", "storage.item.runs.old", "storage.item.runs.recent"),
            ("scene-images", "storage.item.scene_images.old", "storage.item.scene_images.recent"),
        })
        {
            var folder = Path.Combine(work, kind);
            var children = Directory.Exists(folder)
                ? Directory.EnumerateDirectories(folder).Where(d => kind != "scene-images" || !goneConcepts.Contains(Path.GetFileName(d))).ToList()
                : [];
            var old = children.Where(c => clock.GetUtcNow() - Directory.GetLastWriteTimeUtc(c) > Core.Videos.VideoPipeline.WorkKeep).ToList();
            var recent = children.Except(old).ToList();
            yield return Folder("work:" + kind + ":old", oldKey, DiskArea.Work, old, true);
            yield return Folder("work:" + kind + ":recent", recentKey, DiskArea.Work, recent, false);
        }
    }

    /// <summary>
    /// The folders under <paramref name="root"/> that neither are, nor lie inside, nor hold an owned folder: what an older version or another
    /// setting left. Files are never taken (markers next to versions stay).
    /// </summary>
    public static IEnumerable<string> Unowned(string root, IReadOnlyList<string> owned)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(owned);
        if (!Directory.Exists(root))
        {
            yield break;
        }

        var pending = new Stack<string>([Full(root)]);
        while (pending.TryPop(out var folder))
        {
            IEnumerable<string> children;
            try
            {
                children = Directory.EnumerateDirectories(folder).Select(Full).ToList();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var child in children.Order(StringComparer.Ordinal))
            {
                if (owned.Any(o => Same(o, child) || LibraryPaths.Inside(child, o)))
                {
                    continue;
                }

                if (owned.Any(o => LibraryPaths.Inside(o, child)))
                {
                    pending.Push(child);
                    continue;
                }

                yield return child;
            }
        }
    }

    private FolderDiskItem Folder(string key, string nameKey, DiskArea area, IReadOnlyList<string> folders, bool cleanup) =>
        new(key, text[nameKey], area, DiskSizes.Of(folders), folders, cleanup, true);

    private static string Full(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    private static bool Same(string a, string b) =>
        string.Equals(Full(a), Full(b), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}
