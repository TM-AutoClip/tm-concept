using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using TMConcept.Core.Concepts;
using TMConcept.Core.Text;
using TMConcept.Desktop.Concepts;
using TMConcept.Desktop.Localization;
using TMConcept.Desktop.Startup;

namespace TMConcept.Desktop.ViewModels;

/// <summary>
/// Concepts (0.1.13 layout, the developer's request of 11 Nov 2026). On top, one bar shared by the whole page: the models and the
/// fine-tuning as folded one-line summaries, and the word check (<see cref="ConceptSettingsViewModel"/>). Below it two separate sections
/// side by side, each with its own button, estimate, status and Cancel: A "Generate from suggested topics" (<see cref="TopicSectionViewModel"/>)
/// and B "Generate from an idea" (<see cref="IdeaSectionViewModel"/>); one paid call runs at a time (<see cref="ConceptRunner"/>). Whatever
/// either one writes is ADDED to one list, switched between "New" and "Approved" (approved ones can be searched); every row has Approve
/// (or Unapprove) and Delete, and the keys A, D (or Delete) and ↑↓ work on the list. A click opens the concept in the detail panel next to
/// the list (<see cref="ConceptDetailViewModel"/>), never under its row; leaving a concept with unsaved edits asks first (also for another
/// tab or profile; a row's Approve, Unapprove or Delete on that concept too). After a row's action on the open concept the panel moves to
/// the next one, so A / D go through the list; an action on another row leaves the panel where it is. Both lists keep the store's order,
/// the highest number (the newest) first, while batches arrive and after a reload alike.
/// </summary>
public sealed partial class ConceptsViewModel : PageViewModelBase
{
    private readonly AppSession _session;
    private readonly IConceptStore _concepts;
    private readonly ConceptActions _actions;
    private readonly ConceptEvents _events;
    private readonly Localizer _text;
    private readonly ILogger<ConceptsViewModel> _logger;
    private readonly List<ConceptCardViewModel> _approved = [];
    private bool _attached;
    private bool _loadedOnce;
    private bool _choosing;

    /// <summary>Creates the page with its bar, its two sections, its runner and its detail panel.</summary>
    public ConceptsViewModel(AppSession session, IConceptStore concepts, ConceptActions actions, ConceptEvents events, Localizer text,
        ConceptSettingsViewModel settings, ConceptRunner runner, TopicSectionViewModel topics, IdeaSectionViewModel ideas, ConceptDetailViewModel detail,
        ILogger<ConceptsViewModel> logger)
    {
        _session = session;
        _concepts = concepts;
        _actions = actions;
        _events = events;
        _text = text;
        _logger = logger;
        Settings = settings;
        Runner = runner;
        Topics = topics;
        Ideas = ideas;
        Detail = detail;
        Runner.Attach(Settings);
        Topics.Attach(Runner);
        Ideas.Attach(Runner);
        Detail.Attach(Settings.Picker, this);
        Runner.BatchSaved += (_, batch) => AddNew(batch.Accepted);
        Runner.Finished += (_, _) => _ = SafeAsync(ReloadAsync);
        Runner.Changed += (_, _) => UpdateWorking();
        Topics.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(TopicSectionViewModel.Suggesting))
            {
                UpdateWorking();
            }
        };
        Detail.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ConceptDetailViewModel.AiRunning))
            {
                UpdateWorking();
            }
        };
        Detail.Changed += (_, change) => Move(change.Card, change.Status, selectNext: false);
    }

    /// <inheritdoc />
    public override string TitleKey => "nav.concepts";

    /// <inheritdoc />
    public override int Order => 10;

    /// <summary>The bar: models, fine-tuning, word check.</summary>
    public ConceptSettingsViewModel Settings { get; }

    /// <summary>The page's one paid call at a time.</summary>
    public ConceptRunner Runner { get; }

    /// <summary>Section A: from suggested topics.</summary>
    public TopicSectionViewModel Topics { get; }

    /// <summary>Section B: from an idea.</summary>
    public IdeaSectionViewModel Ideas { get; }

    /// <summary>The detail panel.</summary>
    public ConceptDetailViewModel Detail { get; }

    /// <summary>New concepts, the newest first.</summary>
    public ObservableCollection<ConceptCardViewModel> NewConcepts { get; } = [];

    /// <summary>Approved concepts, the newest first (filtered by the search).</summary>
    public ObservableCollection<ConceptCardViewModel> Approved { get; } = [];

    /// <summary>"New (14)".</summary>
    [ObservableProperty]
    public partial string NewHeader { get; set; } = string.Empty;

    /// <summary>"Approved (38)".</summary>
    [ObservableProperty]
    public partial string ApprovedHeader { get; set; } = string.Empty;

    /// <summary>The list shows the approved concepts (otherwise the new ones).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowNew), nameof(EmptyText), nameof(KeysText))]
    public partial bool ShowApproved { get; set; }

    /// <summary>What the keys do on the shown list (A approves a new concept, takes an approved one back).</summary>
    public string KeysText => _text[ShowApproved ? "concepts.list.keys_approved" : "concepts.list.keys"];

    /// <summary>What the empty list says (empty when the shown list has concepts).</summary>
    public string EmptyText => ShowApproved
        ? Approved.Count == 0 ? _text["concepts.list.empty_approved"] : string.Empty
        : NewConcepts.Count == 0 ? _text["concepts.list.empty_new"] : string.Empty;

    /// <summary>The list shows the new concepts.</summary>
    public bool ShowNew => !ShowApproved;

    /// <summary>Search in the approved list.</summary>
    [ObservableProperty]
    public partial string Search { get; set; } = string.Empty;

    /// <summary>The row chosen in the new list.</summary>
    [ObservableProperty]
    public partial ConceptCardViewModel? SelectedNew { get; set; }

    /// <summary>The row chosen in the approved list.</summary>
    [ObservableProperty]
    public partial ConceptCardViewModel? SelectedApproved { get; set; }

    /// <summary>Shows the new concepts.</summary>
    [RelayCommand]
    public void ShowNewList() => SetList(approved: false);

    /// <summary>Shows the approved concepts.</summary>
    [RelayCommand]
    public void ShowApprovedList() => SetList(approved: true);

    /// <summary>Approves a new concept (row button, key A).</summary>
    [RelayCommand]
    public Task ApproveRowAsync(ConceptCardViewModel? card) => RowAsync(card, ConceptStatus.Approved, c => _actions.ApproveAsync(c.Concept, this));

    /// <summary>Takes an approved concept back to the new ones (row button, key A in the approved list).</summary>
    [RelayCommand]
    public Task UnapproveRowAsync(ConceptCardViewModel? card) => RowAsync(card, ConceptStatus.New, c => _actions.UnapproveAsync(c.Concept, this));

    /// <summary>Deletes at once, without a reason (row button, key D or Delete); the detail panel's Delete asks why.</summary>
    [RelayCommand]
    public Task TrashRowAsync(ConceptCardViewModel? card) =>
        RowAsync(card, ConceptStatus.Trashed, c => _actions.TrashAsync([c.Concept], TrashReason.None, this));

    /// <summary>Opens <paramref name="card"/> in the detail panel, after asking when unsaved edits would be lost; false when the user stays.</summary>
    public async Task<bool> ChooseAsync(ConceptCardViewModel? card)
    {
        if (ReferenceEquals(card, Detail.Card))
        {
            return true;
        }

        if (!await Detail.LeaveAsync().ConfigureAwait(true))
        {
            // The user stays: the list shows the open concept again.
            Mark(Detail.Card);
            return false;
        }

        await Detail.LoadAsync(card).ConfigureAwait(true);
        Mark(card);
        return true;
    }

    /// <inheritdoc />
    public override Task<bool> CanLeaveAsync() => Detail.LeaveAsync();

    /// <inheritdoc />
    public override async Task ActivateAsync()
    {
        if (!_attached)
        {
            _attached = true;
            _events.Changed += async (sender, _) =>
            {
                if (!ReferenceEquals(sender, this))
                {
                    await SafeAsync(ReloadAsync).ConfigureAwait(true);
                }
            };
        }

        await Settings.LoadAsync(_session.Profile, newProfile: !_loadedOnce).ConfigureAwait(true);
        _loadedOnce = true;
        await ReloadAsync().ConfigureAwait(true);
    }

    /// <inheritdoc />
    public override async Task ProfileChangedAsync()
    {
        // Everything on the page belonged to the old profile: its lines, its topic list, the open concept (a running AI edit is canceled).
        Status = string.Empty;
        Topics.Clear();
        Ideas.Clear();
        await Detail.LoadAsync(null).ConfigureAwait(true);
        await Settings.LoadAsync(_session.Profile, newProfile: true).ConfigureAwait(true);
        _loadedOnce = true;
        await ReloadAsync().ConfigureAwait(true);
    }

    /// <inheritdoc />
    public override void CancelWork()
    {
        Topics.CancelAll();
        Ideas.Cancel();
        Detail.CancelWork();
    }

    partial void OnSelectedNewChanged(ConceptCardViewModel? value)
    {
        // A row leaving the list sets the selection to nothing: the panel keeps its concept.
        if (value is not null && !_choosing)
        {
            _ = SafeAsync(() => ChooseAsync(value));
        }
    }

    partial void OnSelectedApprovedChanged(ConceptCardViewModel? value)
    {
        if (value is not null && !_choosing)
        {
            _ = SafeAsync(() => ChooseAsync(value));
        }
    }

    partial void OnSearchChanged(string value)
    {
        Filter();
        OnPropertyChanged(nameof(EmptyText));
    }

    private void SetList(bool approved)
    {
        ShowApproved = approved;

        // A click on the segment that is already on unchecks its toggle: telling both again puts it right.
        OnPropertyChanged(nameof(ShowApproved));
        OnPropertyChanged(nameof(ShowNew));
    }

    private async Task RowAsync(ConceptCardViewModel? card, ConceptStatus status, Func<ConceptCardViewModel, Task> change)
    {
        card ??= ShowApproved ? SelectedApproved : SelectedNew;
        if (card is null)
        {
            return;
        }

        // The open concept's unsaved edits are not thrown away without asking; "stay" leaves everything as it is.
        var open = ReferenceEquals(card, Detail.Card);
        if (open && !await Detail.LeaveAsync().ConfigureAwait(true))
        {
            return;
        }

        try
        {
            await change(card).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "The concept's status could not be changed to {Status}", status);
            Status = _text.Format("error.generic", ex.Message);
            return;
        }

        Detail.Follow(card, status);
        Move(card, status, selectNext: open);
    }

    /// <summary>
    /// Moves <paramref name="card"/> to the list of <paramref name="status"/> (or out of both) at its place by number. With
    /// <paramref name="selectNext"/> (a row's action on the open concept) the list it left selects its next concept and the panel shows it,
    /// so A / D can go through the list one by one.
    /// </summary>
    private void Move(ConceptCardViewModel card, ConceptStatus status, bool selectNext)
    {
        var from = NewConcepts.Contains(card) ? NewConcepts : Approved.Contains(card) ? Approved : null;
        var index = from?.IndexOf(card) ?? -1;
        NewConcepts.Remove(card);
        _approved.Remove(card);
        switch (status)
        {
            case ConceptStatus.New:
                NewConcepts.Insert(Place(NewConcepts, card), card);
                break;
            case ConceptStatus.Approved:
                _approved.Insert(Place(_approved, card), card);
                break;
        }

        Filter();
        Headers();
        if (!selectNext || from is null)
        {
            return;
        }

        var next = from.Count == 0 ? null : from[Math.Clamp(index, 0, from.Count - 1)];
        _ = SafeAsync(() => ChooseAsync(next));
    }

    private void AddNew(IReadOnlyList<Concept> accepted)
    {
        foreach (var concept in accepted.Where(c => NewConcepts.All(n => n.Id != c.Id)))
        {
            var card = new ConceptCardViewModel(concept, _text);
            NewConcepts.Insert(Place(NewConcepts, card), card);
        }

        Headers();
    }

    /// <summary>Where <paramref name="card"/> goes in <paramref name="list"/> so that it stays in the store's order (the highest number first).</summary>
    private static int Place(IList<ConceptCardViewModel> list, ConceptCardViewModel card)
    {
        var index = 0;
        while (index < list.Count && list[index].Concept.Number > card.Concept.Number)
        {
            index++;
        }

        return index;
    }

    /// <summary>The list rows match the panel's concept (or nothing), without opening anything again.</summary>
    private void Mark(ConceptCardViewModel? card)
    {
        _choosing = true;
        try
        {
            SelectedNew = card is not null && NewConcepts.Contains(card) ? card : null;
            SelectedApproved = card is not null && Approved.Contains(card) ? card : null;
        }
        finally
        {
            _choosing = false;
        }
    }

    /// <summary>
    /// Reads both lists and rebuilds them without an await in between (a reload and a batch arriving at the same time cannot add a card
    /// twice). Cards still listed keep their object, so the open concept stays open (a video finishing in the background reloads this page).
    /// </summary>
    private async Task ReloadAsync()
    {
        IReadOnlyList<Concept> fresh = [];
        IReadOnlyList<Concept> approved = [];
        var profile = _session.Profile;
        if (profile is not null)
        {
            fresh = await _concepts.ListAsync(profile.Id, [ConceptStatus.New], false, CancellationToken.None).ConfigureAwait(true);
            approved = await _concepts.ListAsync(profile.Id, [ConceptStatus.Approved], false, CancellationToken.None).ConfigureAwait(true);
            if (_session.Profile?.Id != profile.Id)
            {
                return;
            }
        }

        var cards = NewConcepts.Concat(_approved).GroupBy(c => c.Id).ToDictionary(g => g.Key, g => g.First());
        if (Detail.Card is { } open)
        {
            cards.TryAdd(open.Id, open);
        }

        ConceptCardViewModel Card(Concept concept)
        {
            if (!cards.TryGetValue(concept.Id, out var card))
            {
                return new ConceptCardViewModel(concept, _text);
            }

            card.Concept.Status = concept.Status;
            return card;
        }

        CollectionSync.Apply(NewConcepts, fresh.DistinctBy(c => c.Id).Select(Card).ToList());
        _approved.Clear();
        _approved.AddRange(approved.DistinctBy(c => c.Id).Select(Card));
        Filter();
        Headers();
        Mark(Detail.Card);
        if (Detail.Card is { } shown && !NewConcepts.Contains(shown) && !_approved.Contains(shown) && !Detail.Unsaved)
        {
            // Queued, produced or deleted on another page: the panel shows its status (read-only) and the list does not hold it any more.
            await Detail.LoadAsync(shown).ConfigureAwait(true);
        }

        Topics.Refresh();
        Ideas.Refresh();
    }

    private void Filter()
    {
        var search = TurkishFold.Fold(Search);
        CollectionSync.Apply(Approved, _approved.Where(c => search.Length == 0 || TurkishFold.Fold(c.Title).Contains(search, StringComparison.Ordinal)).ToList());
    }

    private void Headers()
    {
        NewHeader = _text.Format("concepts.list.new", NewConcepts.Count);
        ApprovedHeader = _text.Format("concepts.list.approved", _approved.Count);
        OnPropertyChanged(nameof(EmptyText));
    }

    private void UpdateWorking() => Working = Runner.Busy || Topics.Suggesting || Detail.AiRunning;

    private async Task SafeAsync(Func<Task> work)
    {
        try
        {
            await work().ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Concept page update failed");
            Status = _text.Format("error.generic", ex.Message);
        }
    }
}
