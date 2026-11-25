using Microsoft.Extensions.Logging;
using TMConcept.Core.Activity;
using TMConcept.Desktop.Components;
using TMConcept.Desktop.Dialogs;
using TMConcept.Desktop.Localization;
using TMConcept.Desktop.Paths;
using TMConcept.Desktop.Startup;

namespace TMConcept.Desktop.DiskSpace;

/// <inheritdoc cref="IOrphanFileCheck" />
public sealed class OrphanFileCheck(IDiskCleaner cleaner, IConfirmDialog confirm, UiPreferencesFile preferences, IComponentWorkGate gate, AppSession session,
    IActivityLog activity, Localizer text, TimeProvider clock, ILogger<OrphanFileCheck> logger) : IOrphanFileCheck
{
    /// <summary>The least the unused files must take before the start asks (500 MB).</summary>
    public const long DefaultAskFromBytes = 500L * 1024 * 1024;

    /// <summary>The least the unused files must take before the start asks (<see cref="DefaultAskFromBytes"/>; a test sets less).</summary>
    public long AskFromBytes { get; init; } = DefaultAskFromBytes;

    /// <summary>How long a start does not ask again after it asked.</summary>
    public static readonly TimeSpan AskAgainAfter = TimeSpan.FromDays(1);

    /// <summary>The most rows the window lists (the rest are counted).</summary>
    public const int ListMax = 15;

    /// <inheritdoc />
    public async Task<long> RunAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        if (preferences.Read().OrphansAskedAtUtc is { } asked && now - asked < AskAgainAfter && now >= asked)
        {
            return 0;
        }

        if (gate.Busy || session.WorkRunning)
        {
            logger.LogInformation("Unused model files: not looked at (work or an install runs)");
            return 0;
        }

        var report = await Task.Run(() => cleaner.ReportAsync(cancellationToken), cancellationToken).ConfigureAwait(true);
        var orphans = DiskSelection.Orphans(report.Items);
        var total = orphans.Sum(o => o.Bytes);
        logger.LogInformation("Unused model files: {Count} row(s), {Bytes} bytes", orphans.Count, total);
        if (total < AskFromBytes)
        {
            return 0;
        }

        try
        {
            preferences.Change(p => p.OrphansAskedAtUtc = now);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "The time of the unused files question could not be kept; it may be asked again at the next start");
        }

        var size = ViewModels.StorageSettingsViewModel.Size(total, text.Culture);
        var list = string.Join("\n", orphans.OrderByDescending(o => o.Bytes).Take(ListMax)
                .Select(o => "• " + o.Name + " (" + ViewModels.StorageSettingsViewModel.Size(o.Bytes, text.Culture) + ")"))
            + (orphans.Count > ListMax ? "\n" + text.Format("storage.more", orphans.Count - ListMax) : string.Empty);
        var answer = await confirm.AskAsync(text["storage.orphans.start.title"], text.Format("storage.orphans.start.message", list, size),
            text["storage.orphans.start.confirm"], danger: true, cancelText: text["storage.orphans.start.later"]).ConfigureAwait(true);
        if (!answer.Confirmed)
        {
            logger.LogInformation("Unused model files: the user kept them ({Bytes} bytes)", total);
            return 0;
        }

        var outcome = await cleaner.RemoveAsync(orphans, cancellationToken).ConfigureAwait(true);
        if (outcome.Busy)
        {
            activity.Add(ActivityLevel.Warning, text["storage.busy"]);
            return 0;
        }

        var line = text.Format("storage.done", outcome.Removed.Count, ViewModels.StorageSettingsViewModel.Size(outcome.Freed, text.Culture));
        foreach (var (item, reason) in outcome.Failed)
        {
            line += " " + text.Format("storage.item_failed", item.Name, reason);
        }

        activity.Add(outcome.Failed.Count > 0 ? ActivityLevel.Error : ActivityLevel.Info, line);
        return outcome.Freed;
    }
}
