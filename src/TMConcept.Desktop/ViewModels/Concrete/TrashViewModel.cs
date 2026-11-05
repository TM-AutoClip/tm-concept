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

namespace TMConcept.Desktop.ViewModels;

/// <summary>
/// 🗑 Trash: deleted concepts are kept forever (they still count in the duplicate check). "Restore" puts them back to new. "Delete
/// permanently" asks first and then removes the rows; a permanently deleted concept no longer counts anywhere (there is no "deleted" flag).
/// </summary>
public sealed partial class TrashViewModel(AppSession session, IConceptStore concepts, IConfirmDialog confirm, ConceptEvents events, IActivityLog activity,
    Localizer text, ILogger<TrashViewModel> logger) : PageViewModelBase
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

        await concepts.SetStatusAsync(ids, ConceptStatus.New, CancellationToken.None).ConfigureAwait(true);
        activity.Add(ActivityLevel.Info, text.Format("trash.restored", ids.Count));
        events.Raise(this);
        await ReloadAsync().ConfigureAwait(true);
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

        var removed = await concepts.DeletePermanentlyAsync(ids, CancellationToken.None).ConfigureAwait(true);
        logger.LogInformation("{Count} concepts deleted permanently", removed);
        activity.Add(ActivityLevel.Info, text.Format("trash.deleted", removed));
        events.Raise(this);
        await ReloadAsync().ConfigureAwait(true);
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

    private List<Guid> Chosen()
    {
        var ids = _all.Where(c => c.IsChecked).Select(c => c.Id).ToList();
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
        _all.Clear();
        Selected = null;
        if (session.Profile is { } profile)
        {
            foreach (var concept in await concepts.ListAsync(profile.Id, [ConceptStatus.Trashed], false, CancellationToken.None).ConfigureAwait(true))
            {
                _all.Add(new ConceptCardViewModel(concept, text));
            }
        }

        Badge = _all.Count == 0 ? string.Empty : _all.Count.ToString(CultureInfo.InvariantCulture);
        Header = text.Format("trash.header", _all.Count);
        Filter();
    }

    private void Filter()
    {
        var search = TurkishFold.Fold(Search);
        Items.Clear();
        foreach (var card in _all.Where(c => search.Length == 0 || TurkishFold.Fold(c.Title).Contains(search, StringComparison.Ordinal)))
        {
            Items.Add(card);
        }
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
