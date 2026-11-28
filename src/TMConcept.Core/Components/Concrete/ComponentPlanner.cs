using System.Diagnostics;
using System.Runtime.ExceptionServices;
using Microsoft.Extensions.Logging;
using TMConcept.Core.Tools;

namespace TMConcept.Core.Components;

/// <summary>
/// Every component of the app (from the DI sources) and what a set of them needs: <see cref="PlanAsync"/> lists the missing ones with their
/// dependencies first and what they download; <see cref="InstallAsync"/> installs a plan the user confirmed, one component at a time (one
/// install at a time in the whole app); <see cref="Size"/> and <see cref="RemoveAsync"/> serve the installed components list. Every
/// finished install and every removal goes into the install record (<see cref="IInstallRecord"/>, when there is one).
/// 28 Nov 2026 (the developer: "sıfırdan kur ve güncellerde modelleri silecek önce, kalıcı tekte silecek, geri dönüşüm değil, sonra indirecek.
/// yarıda kapattı, son yarım kalanı siler, onu tekrar indirerek devam eder"): an install first writes its components into the install journal
/// (<see cref="IInstallJournal"/>), then, for each one, deletes its old files for good (<see cref="IComponent.ReplacedPaths"/>, only inside
/// the components folder, never what another component names as its own) and only then downloads it; a component leaves the journal once it
/// is ready. <see cref="ClearUnfinishedAsync"/> deletes the files of what a closed app left unfinished, so its next install starts clean.
/// </summary>
public sealed class ComponentPlanner(IEnumerable<IComponentSource> sources, ILogger<ComponentPlanner> logger, IInstallRecord? record = null,
    IInstallJournal? journal = null, DiskSpace.IKeepingWipe? wipe = null, Paths.IAppPaths? paths = null)
{
    private readonly IReadOnlyList<IComponentSource> _sources = [.. sources];
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>
    /// Every component, asked from the sources each time: most sources keep a fixed list, but one may change (the app's own Ollama models
    /// follow what is installed).
    /// </summary>
    public IReadOnlyList<IComponent> All => _sources.SelectMany(s => s.Components).ToList();

    /// <summary>A component by code; null when unknown.</summary>
    public IComponent? Find(string code) => All.FirstOrDefault(c => c.Code == code);

    /// <summary>
    /// Looks at what is on the computer for every component (a suitable Node.js on the PATH counts as installed only after this); cheap after
    /// the first call. Called at start and before a page reads <see cref="IComponent.Ready"/>.
    /// </summary>
    public Task RefreshAllAsync(CancellationToken cancellationToken) => Task.WhenAll(All.Select(c => c.RefreshAsync(cancellationToken)));

    /// <summary>
    /// The plan for <paramref name="codes"/>: each one that is not ready, after its own dependencies that are not ready. A component that is
    /// ready does not pull its dependencies in (an engine that is ready needs nothing). An unknown code is logged and skipped. A component
    /// whose dependency has no download for this computer is unsupported too (its own weights would be downloaded for nothing).
    /// </summary>
    public async Task<InstallPlan> PlanAsync(IEnumerable<string> codes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(codes);
        var missing = new List<IComponent>();
        var unsupported = new List<IComponent>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        async Task VisitAsync(string code)
        {
            if (!seen.Add(code))
            {
                return;
            }

            if (Find(code) is not { } component)
            {
                logger.LogWarning("A setting needs the unknown component {Component}; it is skipped", code);
                return;
            }

            await component.RefreshAsync(cancellationToken).ConfigureAwait(false);
            if (component.Ready)
            {
                return;
            }

            foreach (var dependency in component.DependsOn)
            {
                await VisitAsync(dependency).ConfigureAwait(false);
            }

            // A dependency found on the system meanwhile (a Node.js on the PATH) can make the component ready by itself.
            if (component.Ready)
            {
                return;
            }

            var blocked = component.DependsOn.Any(d => unsupported.Any(u => u.Code == d));
            (component.Supported && !blocked ? missing : unsupported).Add(component);
        }

        foreach (var code in codes.Distinct(StringComparer.Ordinal))
        {
            await VisitAsync(code).ConfigureAwait(false);
        }

        return new InstallPlan(missing, missing.SelectMany(c => c.Downloads()).ToList(), unsupported);
    }

    /// <summary>
    /// Installs the plan's missing components in order (only after the user confirmed it). <paramref name="progress"/> gets the share of the
    /// whole plan and the component's name; <paramref name="steps"/> gets each component's own state (running with its progress text, done,
    /// failed with the reason, skipped) for the install window. A failure skips the components that depend on the failed one, the others
    /// are still installed (one broken download does not hold back an unrelated step); the first failure goes up at the end. A cancellation
    /// stops at once.
    /// </summary>
    public async Task InstallAsync(InstallPlan plan, IProgress<ToolProgress>? progress, CancellationToken cancellationToken,
        IProgress<ComponentStep>? steps = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        var failed = new HashSet<string>(StringComparer.Ordinal);
        ExceptionDispatchInfo? first = null;
        try
        {
            // Written before any old file is deleted: a close from here on leaves these to be cleaned and installed again at the next start.
            Journal(j => j.Begin(plan.Missing.Where(c => !c.Ready).Select(c => c.Code)));
            for (var i = 0; i < plan.Missing.Count; i++)
            {
                var component = plan.Missing[i];
                if (component.Ready)
                {
                    Journal(j => j.Finished(component.Code));
                    steps?.Report(new ComponentStep(i, component.Code, ComponentStepState.Done, 1, string.Empty));
                    continue;
                }

                if (component.DependsOn.FirstOrDefault(failed.Contains) is { } broken)
                {
                    failed.Add(component.Code);
                    logger.LogWarning("{Component} is not installed: {Dependency} could not be installed", component.Code, broken);
                    steps?.Report(new ComponentStep(i, component.Code, ComponentStepState.Skipped, 0, broken));
                    continue;
                }

                var index = i;
                var count = plan.Missing.Count;
                var inner = new Progress<ToolProgress>(p =>
                {
                    progress?.Report(new ToolProgress((index + Math.Clamp(p.Share, 0, 1)) / count, component.Name));
                    steps?.Report(new ComponentStep(index, component.Code, ComponentStepState.Running, Math.Clamp(p.Share, 0, 1), p.Text));
                });
                progress?.Report(new ToolProgress((double)i / count, component.Name));
                steps?.Report(new ComponentStep(i, component.Code, ComponentStepState.Running, 0, string.Empty));
                var watch = Stopwatch.StartNew();
                logger.LogInformation("Installing {Component}", component.Code);
                try
                {
                    // First its old files go for good (an update never keeps the old copy, an unfinished download never resumes), then it downloads.
                    await ClearAsync(component, cancellationToken).ConfigureAwait(false);
                    await component.InstallAsync(inner, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
                {
                    // A time-out that reached here as a cancellation is a failure, not the user's cancel.
                    logger.LogError(ex, "{Component} could not be installed after {Seconds:0.0} s", component.Code, watch.Elapsed.TotalSeconds);
                    failed.Add(component.Code);
                    first ??= ExceptionDispatchInfo.Capture(ex);
                    steps?.Report(new ComponentStep(i, component.Code, ComponentStepState.Failed, 0, ex.Message));
                    continue;
                }

                logger.LogInformation("{Component} installed in {Seconds:0.0} s", component.Code, watch.Elapsed.TotalSeconds);
                Record(component.Code);
                Journal(j => j.Finished(component.Code));
                steps?.Report(new ComponentStep(i, component.Code, ComponentStepState.Done, 1, string.Empty));
            }

            progress?.Report(new ToolProgress(1, string.Empty));
            first?.Throw();
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>The record of what was installed (empty without one).</summary>
    public IReadOnlyList<InstalledComponent> Recorded() => record?.List() ?? [];

    /// <summary>
    /// The codes of the components that were installed: the record's (also when the components folder was deleted or emptied) and those
    /// whose folders hold an install (from before the record existed; they are recorded now). Looks at the disk first.
    /// </summary>
    public async Task<IReadOnlyList<string>> InstalledAsync(CancellationToken cancellationToken)
    {
        await RefreshAllAsync(cancellationToken).ConfigureAwait(false);
        var recorded = Recorded().Select(r => r.Code).ToHashSet(StringComparer.Ordinal);
        var codes = new List<string>();
        foreach (var component in All)
        {
            if (recorded.Contains(component.Code))
            {
                codes.Add(component.Code);
            }
            else if (component.WasInstalled)
            {
                codes.Add(component.Code);
                Record(component.Code, string.Empty);
            }
        }

        return codes;
    }

    /// <summary>
    /// What a closed app (a crash, a power cut, the window closed in the middle) left unfinished: each journal entry that is ready now leaves the
    /// journal; the old and half-written files of every other one are deleted for good (<see cref="IComponent.ReplacedPaths"/>; the finished
    /// components stay as they are, and so do the files of one that is not ready only because a dependency is missing). Returns the codes still to install, in the journal's order; they stay in the journal until installed.
    /// </summary>
    public async Task<IReadOnlyList<string>> ClearUnfinishedAsync(CancellationToken cancellationToken)
    {
        if (journal is null)
        {
            return [];
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var left = new List<string>();
            foreach (var code in journal.Unfinished())
            {
                if (Find(code) is not { } component)
                {
                    logger.LogWarning("The unfinished install of the unknown component {Component} is forgotten", code);
                    Journal(j => j.Finished(code));
                    continue;
                }

                await component.RefreshAsync(cancellationToken).ConfigureAwait(false);
                if (component.Ready)
                {
                    // It was ready before the app closed; only its journal line was left.
                    Journal(j => j.Finished(code));
                    continue;
                }

                await ClearAsync(component, cancellationToken).ConfigureAwait(false);
                left.Add(code);
            }

            if (left.Count > 0)
            {
                logger.LogInformation("Unfinished installs of a previous run: {Codes}; their files were deleted, they are downloaded again", string.Join(", ", left));
            }

            return left;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>The codes the install journal names as unfinished (empty without a journal).</summary>
    public IReadOnlyList<string> Unfinished() => journal?.Unfinished() ?? [];

    /// <summary>Takes <paramref name="codes"/> out of the install journal (the user chose to install them later, when a step needs them).</summary>
    public void Forget(IEnumerable<string> codes)
    {
        ArgumentNullException.ThrowIfNull(codes);
        foreach (var code in codes)
        {
            Journal(j => j.Finished(code));
        }
    }

    /// <summary>
    /// Deletes for good what an install of <paramref name="component"/> replaces, before its download: only paths strictly inside the
    /// components folder (the database, the key file and the library are never reached), and never a folder another component names as its
    /// own or shared, nor this component's shared folders (each is kept, everything around it goes). Nothing when the component has nothing
    /// of its own to download (it waits only for a dependency), and nothing without the wipe and the paths.
    /// </summary>
    private async Task ClearAsync(IComponent component, CancellationToken cancellationToken)
    {
        if (wipe is null || paths is null)
        {
            return;
        }

        // A component with nothing of its own to download is not ready only because of a dependency (an image model whose weights are
        // complete while a new stable-diffusion.cpp build is pinned, a voice whose libraries are missing): its files stay, otherwise its
        // install would download gigabytes again that the confirmation never listed.
        if (component.Downloads().Count == 0)
        {
            return;
        }

        var tools = paths.ToolsDirectory;
        var keep = All.Where(c => c.Code != component.Code).SelectMany(c => c.Folders().Concat(c.SharedFolders()))
            .Concat(component.SharedFolders()).ToList();
        var count = 0;
        foreach (var path in component.ReplacedPaths().Distinct(StringComparer.Ordinal))
        {
            if (!Paths.LibraryPaths.Inside(path, tools))
            {
                logger.LogWarning("{Component}: a path outside the components folder is not deleted before its install", component.Code);
                continue;
            }

            count += await wipe.WipeAsync(path, keep, cancellationToken).ConfigureAwait(false);
        }

        if (count > 0)
        {
            logger.LogInformation("{Component}: {Count} old folder(s) or file(s) deleted permanently before the download", component.Code, count);
        }
    }

    /// <summary>Writes to the install journal; a failure is logged (the install itself goes on).</summary>
    private void Journal(Action<IInstallJournal> write)
    {
        if (journal is null)
        {
            return;
        }

        try
        {
            write(journal);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Without the line a close in the middle is not known at the next start; the component is still offered when a step needs it.
            logger.LogWarning(ex, "The install journal could not be written");
        }
    }

    private void Record(string code, string? appVersion = null)
    {
        try
        {
            record?.Installed(code, appVersion);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The install itself is done; only the next "update after a new version" may not know it.
            logger.LogWarning(ex, "The install of {Component} could not be recorded", code);
        }
    }

    /// <summary>Bytes on disk of a component's folders and files (0 when it has none or they cannot be read).</summary>
    public long Size(IComponent component)
    {
        ArgumentNullException.ThrowIfNull(component);
        long total = 0;
        foreach (var folder in component.Folders())
        {
            try
            {
                // A component may own single files too (an Ollama model: its manifest and blobs).
                total += Directory.Exists(folder)
                    ? new DirectoryInfo(folder).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length)
                    : File.Exists(folder) ? new FileInfo(folder).Length : 0;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(ex, "The size of {Component} could not be read", component.Code);
            }
        }

        return total;
    }

    /// <summary>
    /// Whether an install or a removal runs now (the storage manager refuses to clean up meanwhile: a download in progress is not a
    /// leftover).
    /// </summary>
    public bool Busy => _gate.CurrentCount == 0;

    /// <summary>
    /// Removes a component: each of its folders is deleted for good through <paramref name="discard"/> (the user confirmed it
    /// first) under the install gate, then the install record forgets it. It is downloaded again the next time a setting needs it.
    /// The caller ends the speech and Python workers and frees the graphics card first (the storage manager does). A folder that could not
    /// be taken stops the removal with the error; what was taken stays taken and the record still names the component until all is gone.
    /// </summary>
    public async Task RemoveAsync(IComponent component, DiskSpace.IFolderDiscard discard, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(component);
        ArgumentNullException.ThrowIfNull(discard);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            foreach (var folder in component.Folders().Where(f => Directory.Exists(f) || File.Exists(f)))
            {
                await discard.DiscardAsync(folder, cancellationToken).ConfigureAwait(false);
            }

            logger.LogInformation("{Component} removed (deleted permanently)", component.Code);
            try
            {
                record?.Removed(component.Code);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(ex, "The removal of {Component} could not be recorded", component.Code);
            }
        }
        finally
        {
            _gate.Release();
        }
    }
}
