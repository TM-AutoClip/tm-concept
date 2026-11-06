using System.Diagnostics;
using Microsoft.Extensions.Logging;
using TMConcept.Core.Tools;

namespace TMConcept.Core.Components;

/// <summary>
/// Every component of the app (from the DI sources) and what a set of them needs: <see cref="PlanAsync"/> lists the missing ones with their
/// dependencies first and what they download; <see cref="InstallAsync"/> installs a plan the user confirmed, one component at a time (one
/// install at a time in the whole app); <see cref="Size"/> and <see cref="RemoveAsync"/> serve the installed components list.
/// </summary>
public sealed class ComponentPlanner(IEnumerable<IComponentSource> sources, ILogger<ComponentPlanner> logger)
{
    private readonly Lazy<IReadOnlyList<IComponent>> _all = new(() => sources.SelectMany(s => s.Components).ToList());
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>Every component.</summary>
    public IReadOnlyList<IComponent> All => _all.Value;

    /// <summary>A component by code; null when unknown.</summary>
    public IComponent? Find(string code) => All.FirstOrDefault(c => c.Code == code);

    /// <summary>
    /// The plan for <paramref name="codes"/>: each one that is not ready, after its own dependencies that are not ready. A component that is
    /// ready does not pull its dependencies in (an engine that is ready needs nothing). An unknown code is logged and skipped.
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

            (component.Supported ? missing : unsupported).Add(component);
        }

        foreach (var code in codes.Distinct(StringComparer.Ordinal))
        {
            await VisitAsync(code).ConfigureAwait(false);
        }

        return new InstallPlan(missing, missing.SelectMany(c => c.Downloads()).ToList(), unsupported);
    }

    /// <summary>
    /// Installs the plan's missing components in order (only after the user confirmed it). <paramref name="progress"/> gets the share of the
    /// whole plan and the component's name. The first failure stops the plan and goes up (the components before it stay installed).
    /// </summary>
    public async Task InstallAsync(InstallPlan plan, IProgress<ToolProgress>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            for (var i = 0; i < plan.Missing.Count; i++)
            {
                var component = plan.Missing[i];
                if (component.Ready)
                {
                    continue;
                }

                var index = i;
                var count = plan.Missing.Count;
                var inner = new Progress<ToolProgress>(p => progress?.Report(new ToolProgress((index + Math.Clamp(p.Share, 0, 1)) / count, component.Name)));
                progress?.Report(new ToolProgress((double)i / count, component.Name));
                var watch = Stopwatch.StartNew();
                logger.LogInformation("Installing {Component}", component.Code);
                try
                {
                    await component.InstallAsync(inner, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogError(ex, "{Component} could not be installed after {Seconds:0.0} s", component.Code, watch.Elapsed.TotalSeconds);
                    throw;
                }

                logger.LogInformation("{Component} installed in {Seconds:0.0} s", component.Code, watch.Elapsed.TotalSeconds);
            }

            progress?.Report(new ToolProgress(1, string.Empty));
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Bytes on disk of a component's folders (0 when it has none or they cannot be read).</summary>
    public long Size(IComponent component)
    {
        ArgumentNullException.ThrowIfNull(component);
        long total = 0;
        foreach (var folder in component.Folders().Where(Directory.Exists))
        {
            try
            {
                total += new DirectoryInfo(folder).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(ex, "The size of {Component} could not be read", component.Code);
            }
        }

        return total;
    }

    /// <summary>Removes a component's folders (it is downloaded again the next time a setting needs it).</summary>
    public async Task RemoveAsync(IComponent component, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(component);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            foreach (var folder in component.Folders().Where(Directory.Exists))
            {
                await Task.Run(() => Directory.Delete(folder, recursive: true), cancellationToken).ConfigureAwait(false);
            }

            logger.LogInformation("{Component} removed", component.Code);
        }
        finally
        {
            _gate.Release();
        }
    }
}
