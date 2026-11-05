using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using TMConcept.Core.Activity;
using TMConcept.Core.Concepts;
using TMConcept.Core.Text;
using TMConcept.Desktop.Dialogs;
using TMConcept.Desktop.Localization;
using TMConcept.Desktop.Startup;
using TMConcept.Storage.Backup;

namespace TMConcept.Desktop.ViewModels;

/// <summary>
/// 🗑 Trash: deleted concepts are kept forever (they still count in the duplicate check). "Restore" puts them back to new. "Delete
/// permanently" asks first and then removes the rows; a permanently deleted concept no longer counts anywhere (there is no "deleted" flag).
/// Deleting <see cref="BackupRules.BulkChange"/> or more takes a backup first. Only checked cards that are visible (not hidden by the
/// search) are acted on. A storage error is shown and logged, never a crash.
/// </summary>
public sealed partial class TrashViewModel(AppSession session, IConceptStore concepts, IConfirmDialog confirm, ConceptEvents events, IActivityLog activity,
    BackupService backup, Localizer text, ILogger<TrashViewModel> logger) : PageViewModelBase
{
    private readonly List<ConceptCardViewModel> _all = [];
    private bool _attached;

    /// <inheritdoc />
    public override string TitleKey => "nav.trash";

    /// <inheritdoc />
    public override string Glyph => "🗑";

    /// <inheritdoc />
    public override int Order => 40;

    /// <summary>Concepts in the trash (filtered).</summary>
    public ObservableCollection<ConceptCardViewModel> Items { get; } = [];

    /// <summary>Search.</summary>
    [ObservableProperty]
    public partial string Search { get; set; } = string.Empty;

    /// <summary>Open card.</summary>
    [ObservableProperty]
    public partial ConceptCardViewModel? Selected { get; set; }

    /// <summary>"Trash (12)".</summary>
    [ObservableProperty]
    public partial string Header { get; set; } = string.Empty;

    /// <inheritdoc />
    public override Task ActivateAsync()
    {
        Attach();
        return ReloadAsync();
    }

    /// <inheritdoc />
    public override Task ProfileChangedAsync()
    {
        Attach();
        return ReloadAsync();
    }

    /// <summary>Restores the checked concepts (or the open one) to new.</summary>
    [RelayCommand]
    public async Task RestoreAsync()
    {
        var ids = Chosen();
        if (ids.Count == 0)
        {
            return;
        }

        try
        {
            await concepts.SetStatusAsync(ids, ConceptStatus.New, CancellationToken.None).ConfigureAwait(true);
            activity.Add(ActivityLevel.Info, text.Format("trash.restored", ids.Count));
            events.Raise(this);
            await ReloadAsync().ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Concepts could not be restored from the trash");
            Status = text.Format("error.generic", ex.Message);
        }
    }

    /// <summary>Deletes the checked concepts permanently after a confirmation.</summary>
    [RelayCommand]
    public async Task DeletePermanentlyAsync()
    {
        var ids = Chosen();
        if (ids.Count == 0)
        {
            return;
        }

        var answer = await confirm.AskAsync(text["trash.delete.title"], text.Format("trash.delete.message", ids.Count), text["trash.delete.confirm"],
            danger: true).ConfigureAwait(true);
        if (!answer.Confirmed)
        {
            return;
        }

        try
        {
            if (ids.Count >= BackupRules.BulkChange && await backup.CreateBeforeChangeAsync("permanent delete", CancellationToken.None).ConfigureAwait(true) is not null)
            {
                activity.Add(ActivityLevel.Info, text["backup.before_change"]);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Without the safety backup nothing is deleted: the user can try again (or free disk space).
            logger.LogError(ex, "The backup before a permanent delete failed; nothing was deleted");
            Status = text.Format("backup.before_change_failed", ex.Message);
            return;
        }

        try
        {
            var removed = await concepts.DeletePermanentlyAsync(ids, CancellationToken.None).ConfigureAwait(true);
            logger.LogInformation("{Count} concepts deleted permanently", removed);
            activity.Add(ActivityLevel.Info, text.Format("trash.deleted", removed));
            events.Raise(this);
            await ReloadAsync().ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Concepts could not be deleted permanently");
            Status = text.Format("error.generic", ex.Message);
        }
    }

    partial void OnSearchChanged(string value) => Filter();

    partial void OnSelectedChanged(ConceptCardViewModel? oldValue, ConceptCardViewModel? newValue)
    {
        oldValue?.Expanded = false;
        if (newValue is not null)
        {
            newValue.Expanded = true;
            _ = LoadContentAsync(newValue);
        }
    }

    /// <summary>The checked cards that are visible now (a card hidden by the search is never acted on), else the open one.</summary>
    private List<Guid> Chosen()
    {
        var ids = Items.Where(c => c.IsChecked).Select(c => c.Id).ToList();
        if (ids.Count == 0 && Selected is not null)
        {
            ids.Add(Selected.Id);
        }

        return ids;
    }

    private void Attach()
    {
        if (_attached)
        {
            return;
        }

        _attached = true;
        events.Changed += async (sender, _) =>
        {
            if (!ReferenceEquals(sender, this))
            {
                try
                {
                    await ReloadAsync().ConfigureAwait(true);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogError(ex, "The trash could not be reloaded");
                }
            }
        };
    }

    private async Task ReloadAsync()
    {
        var profile = session.Profile;
        var list = profile is null ? [] : await concepts.ListAsync(profile.Id, [ConceptStatus.Trashed], false, CancellationToken.None).ConfigureAwait(true);
        if (session.Profile?.Id != profile?.Id)
        {
            return;
        }

        var cards = _all.ToDictionary(c => c.Id);
        var selected = Selected?.Id;
        _all.Clear();
        _all.AddRange(list.Select(c => cards.TryGetValue(c.Id, out var card) ? card : new ConceptCardViewModel(c, text)));

        Badge = _all.Count == 0 ? string.Empty : _all.Count.ToString(CultureInfo.InvariantCulture);
        Header = text.Format("trash.header", _all.Count);
        Filter();
        var again = Items.FirstOrDefault(c => c.Id == selected);
        if (!ReferenceEquals(again, Selected))
        {
            Selected = again;
        }
    }

    private void Filter()
    {
        var search = TurkishFold.Fold(Search);
        CollectionSync.Apply(Items, _all.Where(c => search.Length == 0 || TurkishFold.Fold(c.Title).Contains(search, StringComparison.Ordinal)).ToList());
    }

    private async Task LoadContentAsync(ConceptCardViewModel card)
    {
        try
        {
            if (card.Lines.Count == 0 && await concepts.GetAsync(card.Id, CancellationToken.None).ConfigureAwait(true) is { } full)
            {
                card.ShowContent(full.Content);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "A concept could not be read");
            Status = text.Format("error.generic", ex.Message);
        }
    }
}
