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
/// </summary>
public sealed class ComponentPlanner(IEnumerable<IComponentSource> sources, ILogger<ComponentPlanner> logger, IInstallRecord? record = null)
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
            for (var i = 0; i < plan.Missing.Count; i++)
            {
                var component = plan.Missing[i];
                if (component.Ready)
                {
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
    /// The codes of the components that were installed: the record's (also when the components folder was set aside or emptied) and those
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
