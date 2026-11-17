using Microsoft.Extensions.Logging;
using TMConcept.Core.Components;
using TMConcept.Core.DiskSpace;
using TMConcept.Core.Models;
using TMConcept.Desktop.Dialogs;
using TMConcept.Desktop.Localization;
using TMConcept.Desktop.Paths;
using TMConcept.Desktop.Startup;

namespace TMConcept.Desktop.DiskSpace;

/// <summary>
/// <see cref="IDiskCleaner"/>. A removal is refused while work runs (a video, a generation, an install, an engine's sample render:
/// <see cref="AppSession.WorkRunning"/>, <see cref="ComponentPlanner.Busy"/>, <see cref="Engines.Engines.SampleRender.Busy"/>, a document being added
/// or vectorized: <see cref="Core.Documents.DocumentLibrary.Busy"/>) or while a worker
/// is speaking; otherwise the idle speech and Python workers are ended (they hold the
/// voice files open), the graphics card is freed and the app's own Ollama server is ended, then every row goes to the recycle bin. Rows the bin could not take are listed in one
/// question; only a "Delete permanently" there deletes them for good, otherwise they stay. Failures are logged as errors with the reason.
/// </summary>
public sealed class DiskCleaner(IEnumerable<IDiskItemSource> sources, IComponentUsage usage, ComponentPlanner planner, AppSession session,
    Speech.Speech.SpeechWorkerClient speech, Speech.Python.PythonWorkerClient python, IGpuCoordinator gpu, RecycleBinDiscard bin, PermanentDiscard permanent,
    IConfirmDialog confirm, AppPaths paths, Localizer text, Engines.Engines.SampleRender sample, Core.AppOllama.IAppOllamaServer appOllama,
    Core.Documents.DocumentLibrary documents, ILogger<DiskCleaner> logger) : IDiskCleaner
{
    /// <inheritdoc />
    public async Task<DiskReport> ReportAsync(CancellationToken cancellationToken)
    {
        var map = await usage.ComputeAsync(cancellationToken).ConfigureAwait(false);
        var items = new List<IDiskItem>();
        foreach (var source in sources)
        {
            items.AddRange(await source.ListAsync(map, cancellationToken).ConfigureAwait(false));
        }

        var totals = await Task.Run(() => Totals(items), cancellationToken).ConfigureAwait(false);
        return new DiskReport(items, totals);
    }

    /// <inheritdoc />
    public IDiskItem ItemOf(IComponent component)
    {
        ArgumentNullException.ThrowIfNull(component);
        return new ComponentDiskItem(component, planner, planner.Size(component), component.Ready ? DiskItemState.Unused : DiskItemState.Incomplete, []);
    }

    /// <inheritdoc />
    public async Task<DiskRemoval> RemoveAsync(IReadOnlyList<IDiskItem> items, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(items);
        var chosen = items.Where(i => i.Removable).ToList();
        if (session.WorkRunning || planner.Busy || sample.Busy || documents.Busy)
        {
            logger.LogInformation("Storage: nothing was removed, work is running");
            return DiskRemoval.Refused;
        }

        // The speech workers keep the voice files open for a minute after the last scene; one that is speaking is waited for by the user.
        if (!await Task.Run(() => speech.StopIfIdle() & python.StopIfIdle(), CancellationToken.None).ConfigureAwait(false))
        {
            logger.LogInformation("Storage: nothing was removed, a voice is speaking");
            return DiskRemoval.Refused;
        }

        await gpu.FreeAsync(cancellationToken).ConfigureAwait(false);

        // The app's own Ollama server keeps its program (and on Windows a loaded model's files) open: it is ended first and starts again
        // on the next use.
        await appOllama.StopAsync(cancellationToken).ConfigureAwait(false);
        logger.LogInformation("Storage: removing {Count} item(s) ({Keys})", chosen.Count, string.Join(", ", chosen.Select(i => i.Key)));
        var removed = new List<IDiskItem>();
        var noBin = new List<(IDiskItem Item, string Reason)>();
        foreach (var item in chosen)
        {
            try
            {
                await item.RemoveAsync(bin, cancellationToken).ConfigureAwait(false);
                removed.Add(item);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException
                or OperationCanceledException { CancellationToken.IsCancellationRequested: false } && !cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Storage: {Key} could not be moved to the recycle bin", item.Key);
                noBin.Add((item, ex.Message));
            }
        }

        var kept = new List<IDiskItem>();
        var failed = new List<(IDiskItem Item, string Reason)>();
        if (noBin.Count > 0)
        {
            var list = string.Join("\n", noBin.Select(n => "• " + n.Item.Name + " (" + n.Reason + ")"));
            var answer = await confirm.AskAsync(text["storage.bin_failed.title"], text.Format("storage.bin_failed", list), text["storage.delete_permanently"],
                danger: true).ConfigureAwait(false);
            foreach (var (item, _) in noBin)
            {
                if (!answer.Confirmed)
                {
                    kept.Add(item);
                    continue;
                }

                try
                {
                    await item.RemoveAsync(permanent, cancellationToken).ConfigureAwait(false);
                    removed.Add(item);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    logger.LogError(ex, "Storage: {Key} could not be deleted", item.Key);
                    failed.Add((item, ex.Message));
                }
            }

            if (!answer.Confirmed)
            {
                logger.LogInformation("Storage: the permanent delete was declined; {Count} item(s) stay", kept.Count);
            }
        }

        logger.LogInformation("Storage: {Removed} removed ({Megabytes} MB), {Kept} kept, {Failed} failed", removed.Count, removed.Sum(i => i.Bytes) >> 20,
            kept.Count, failed.Count);
        return new DiskRemoval(false, removed, kept, failed);
    }

    /// <summary>
    /// The totals by area: tools = the components folder without the models, plus old components folders next to it; models = the model
    /// rows; work, backups and logs (with problem reports) as their folders.
    /// </summary>
    private List<DiskAreaTotal> Totals(IReadOnlyList<IDiskItem> items)
    {
        var models = items.Where(i => i.Area == DiskArea.Models).Sum(i => i.Bytes);
        var aside = items.Where(i => i.Key.StartsWith("tools:aside:", StringComparison.Ordinal)).Sum(i => i.Bytes);
        var tools = Math.Max(0, DiskSizes.Of(paths.ToolsDirectory) - models) + aside;
        var documents = items.Where(i => i.Area == DiskArea.Documents).Sum(i => i.Bytes);
        List<DiskAreaTotal> totals =
        [
            new(DiskArea.Tools, tools),
            new(DiskArea.Models, models),
            new(DiskArea.Work, DiskSizes.Of(paths.WorkDirectory)),
            new(DiskArea.Backups, DiskSizes.Of(paths.BackupDirectory)),
            new(DiskArea.Logs, DiskSizes.Of(paths.LogDirectory) + DiskSizes.Of(paths.ReportDirectory)),
        ];
        if (documents > 0)
        {
            totals.Add(new DiskAreaTotal(DiskArea.Documents, documents));
        }

        return totals;
    }
}
