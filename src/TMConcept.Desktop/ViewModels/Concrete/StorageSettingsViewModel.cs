using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using TMConcept.Core.Activity;
using TMConcept.Core.DiskSpace;
using TMConcept.Desktop.Dialogs;
using TMConcept.Desktop.DiskSpace;
using TMConcept.Desktop.Localization;

namespace TMConcept.Desktop.ViewModels;

/// <summary>
/// Settings → Storage (the developer, 17 Nov 2026: the app starts small and downloads what is needed later, so the user must see and free
/// the space): the totals by area (tools, models, work caches, backups, logs) and every installed component and leftover with its size, its
/// state (used / unused / incomplete / can be cleaned) and who uses it (profile / step, from every profile's saved choices). "Remove
/// selected" (several at once), "Clean up unused" (unused components and leftovers that come back by themselves; never a used one) and
/// "Full delete" (every component and cache; the database, profiles, concepts, videos, backups and logs stay). Each one asks first, goes
/// to the recycle bin, and is refused while work runs.
/// </summary>
public sealed partial class StorageSettingsViewModel(IDiskCleaner cleaner, Localizer text, IConfirmDialog confirm, IActivityLog activity,
    ILogger<StorageSettingsViewModel> logger) : SettingsSectionBase
{
    /// <inheritdoc />
    public override string TitleKey => "settings.storage";

    /// <inheritdoc />
    public override int Order => 15;

    /// <summary>The rows (components first, then leftovers).</summary>
    public ObservableCollection<DiskItemRowViewModel> Items { get; } = [];

    /// <summary>The totals by area.</summary>
    public ObservableCollection<DiskTotalLine> Totals { get; } = [];

    /// <summary>Looking or removing.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Idle))]
    public partial bool Busy { get; set; }

    /// <summary>Not busy.</summary>
    public bool Idle => !Busy;

    /// <summary>Nothing takes space yet.</summary>
    [ObservableProperty]
    public partial bool Empty { get; set; }

    /// <inheritdoc />
    public override Task ActivateAsync() => RefreshAsync();

    /// <summary>Looks at the disk again.</summary>
    [RelayCommand]
    public async Task RefreshAsync()
    {
        if (Busy)
        {
            return;
        }

        Busy = true;
        try
        {
            await LoadAsync().ConfigureAwait(true);
        }
        finally
        {
            Busy = false;
        }
    }

    /// <summary>Removes the ticked rows after a confirmation.</summary>
    [RelayCommand]
    public Task RemoveSelectedAsync() =>
        RemoveAsync(Items.Where(r => r.IsSelected && r.CanSelect).Select(r => r.Item).ToList(), "storage.remove.title", "storage.remove.message");

    /// <summary>Removes the unused components and the leftovers that come back by themselves (never a used one) after a confirmation.</summary>
    [RelayCommand]
    public Task CleanUnusedAsync() => RemoveAsync(DiskSelection.Unused(Items.Select(r => r.Item)), "storage.clean.title", "storage.clean.message");

    /// <summary>Removes every component and cache after a confirmation that says what stays.</summary>
    [RelayCommand]
    public Task FullDeleteAsync() => RemoveAsync(DiskSelection.Full(Items.Select(r => r.Item)), "storage.full.title", "storage.full.message");

    /// <summary>"1.2 GB", "312 MB", "4 KB".</summary>
    public static string Size(long bytes, CultureInfo culture)
    {
        const double Kilo = 1024;
        return bytes switch
        {
            >= 1L << 30 => (bytes / (Kilo * Kilo * Kilo)).ToString("0.0", culture) + " GB",
            >= 1L << 20 => Math.Round(bytes / (Kilo * Kilo)).ToString("0", culture) + " MB",
            _ => Math.Ceiling(bytes / Kilo).ToString("0", culture) + " KB",
        };
    }

    /// <summary>"Used by: Ottoman history / Narration, the app itself".</summary>
    internal string UsedBy(IReadOnlyList<DiskUse> uses)
    {
        if (uses.Count == 0)
        {
            return string.Empty;
        }

        var parts = uses.Select(u => u.IsApp ? text["storage.used_by.app"] : u.ProfileName + " / " + StepName(u.Step)).Distinct(StringComparer.Ordinal);
        return text.Format("storage.used_by", string.Join(", ", parts));
    }

    private string StepName(string step) => text.Has("step." + step) ? text["step." + step] : step;

    private async Task LoadAsync()
    {
        var report = await Task.Run(() => cleaner.ReportAsync(CancellationToken.None)).ConfigureAwait(true);
        var selected = Items.Where(r => r.IsSelected).Select(r => r.Item.Key).ToHashSet(StringComparer.Ordinal);
        Items.Clear();
        foreach (var item in report.Items.OrderBy(i => i.State == DiskItemState.Leftover).ThenBy(i => i.Area).ThenByDescending(i => i.Bytes))
        {
            Items.Add(new DiskItemRowViewModel(item, StateText(item.State), Size(item.Bytes, text.Culture), UsedBy(item.UsedBy))
            {
                IsSelected = selected.Contains(item.Key) && item.Removable,
            });
        }

        Totals.Clear();
        foreach (var total in report.Totals)
        {
            Totals.Add(new DiskTotalLine(text["storage.area." + total.Area.ToString().ToLowerInvariant()], Size(total.Bytes, text.Culture)));
        }

        Empty = Items.Count == 0;
    }

    private string StateText(DiskItemState state) => text["storage.state." + state.ToString().ToLowerInvariant()];

    private async Task RemoveAsync(IReadOnlyList<IDiskItem> items, string titleKey, string messageKey)
    {
        if (Busy)
        {
            return;
        }

        if (items.Count == 0)
        {
            Status = text["storage.nothing"];
            return;
        }

        var list = string.Join("\n", items.Take(20).Select(i => "• " + i.Name + " (" + Size(i.Bytes, text.Culture) + ")"))
            + (items.Count > 20 ? "\n" + text.Format("storage.more", items.Count - 20) : string.Empty);
        var answer = await confirm.AskAsync(text[titleKey], text.Format(messageKey, list, Size(items.Sum(i => i.Bytes), text.Culture)),
            text["storage.confirm"], danger: true).ConfigureAwait(true);
        if (!answer.Confirmed)
        {
            return;
        }

        Busy = true;
        try
        {
            var outcome = await cleaner.RemoveAsync(items, CancellationToken.None).ConfigureAwait(true);
            Status = Report(outcome);
            activity.Add(outcome.Busy || outcome.Kept.Count > 0 ? ActivityLevel.Warning : outcome.Failed.Count > 0 ? ActivityLevel.Error : ActivityLevel.Info, Status);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            // Not the recycle bin's refusal (that is asked about): a folder in use, a path outside the data folder. Shown, logged red, stops.
            logger.LogError(ex, "Storage: the removal stopped");
            Status = text.Format("storage.failed", ex.Message);
            activity.Add(ActivityLevel.Error, Status);
        }
        finally
        {
            Busy = false;
        }

        await RefreshAsync().ConfigureAwait(true);
    }

    private string Report(DiskRemoval outcome)
    {
        if (outcome.Busy)
        {
            return text["storage.busy"];
        }

        var lines = new List<string> { text.Format("storage.done", outcome.Removed.Count, Size(outcome.Freed, text.Culture)) };
        if (outcome.Kept.Count > 0)
        {
            lines.Add(text.Format("storage.kept", string.Join(", ", outcome.Kept.Select(i => i.Name))));
        }

        lines.AddRange(outcome.Failed.Select(f => text.Format("storage.item_failed", f.Item.Name, f.Reason)));
        return string.Join(" ", lines);
    }
}
