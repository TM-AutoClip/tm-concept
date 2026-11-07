using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using TMConcept.Core.Activity;
using TMConcept.Core.Concepts;
using TMConcept.Core.Models;
using TMConcept.Core.Notifications;
using TMConcept.Core.Pipeline;
using TMConcept.Core.Text;
using TMConcept.Desktop.Localization;
using TMConcept.Desktop.Startup;

namespace TMConcept.Desktop.ViewModels;

/// <summary>
/// 💡 Generate concepts: count (1–100), extra request, the concept model (with its effort) and the optional semantic duplicate check
/// (local or online embedding) chosen right here — the selected profile's last choices are shown, a new profile starts with none; local
/// duplicate check. The estimate is recalculated when an input or a model changes; after "Generate" the same box shows the exact cost.
/// Generate first makes the choices ready (license, an API key asked for in a dialog when the provider has none, installs of a local model)
/// and saves them for the profile; anything missing stops it before anything is paid for. Concepts arrive batch by batch; a card shows
/// title and summary, opening it shows the full text; ✓ (A) approves, ✗ (D) moves to the trash. Thrown-away duplicates are not replaced
/// automatically: a "Fill the missing" button appears.
/// </summary>
public sealed partial class ConceptsViewModel(AppSession session, IConceptStore concepts, ConceptGenerator generator, StepModelResolver resolver,
    IModelCatalog catalog, ConceptEvents events, INotifier notifier, IActivityLog activity, Localizer text, StepPickerViewModel picker,
    ILogger<ConceptsViewModel> logger) : PageViewModelBase
{
    private readonly List<ConceptCardViewModel> _approved = [];
    private CancellationTokenSource? _cancel;
    private bool _attached;
    private bool _loading;
    private bool _pickerAttached;

    /// <inheritdoc />
    public override string TitleKey => "nav.concepts";

    /// <inheritdoc />
    public override string Glyph => "💡";

    /// <inheritdoc />
    public override int Order => 10;

    /// <summary>How many concepts.</summary>
    [ObservableProperty]
    public partial decimal? Count { get; set; } = 20;

    /// <summary>Extra request.</summary>
    [ObservableProperty]
    public partial string ExtraPrompt { get; set; } = string.Empty;

    /// <summary>The models of this page: the concept model and the semantic duplicate check (the profile's last choices).</summary>
    public StepPickerViewModel Picker { get; } = picker;

    /// <summary>Local duplicate check.</summary>
    [ObservableProperty]
    public partial bool LocalCheck { get; set; } = true;

    /// <summary>Estimate.</summary>
    [ObservableProperty]
    public partial string Estimate { get; set; } = string.Empty;

    /// <summary>Exact cost of the last press.</summary>
    [ObservableProperty]
    public partial string LastCost { get; set; } = string.Empty;

    /// <summary>Price table date.</summary>
    [ObservableProperty]
    public partial string PricedOn { get; set; } = string.Empty;

    /// <summary>Running.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Idle))]
    public partial bool Busy { get; set; }

    /// <summary>Not running.</summary>
    public bool Idle => !Busy;

    /// <summary>Batch progress.</summary>
    [ObservableProperty]
    public partial string Progress { get; set; } = string.Empty;

    /// <summary>How many are missing after the last press.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMissing), nameof(FillMissingText))]
    public partial int Missing { get; set; }

    /// <summary>Whether "Fill the missing" is shown.</summary>
    public bool HasMissing => Missing > 0 && !Busy;

    /// <summary>Text of the "Fill the missing" button.</summary>
    public string FillMissingText => text.Format("concepts.fill_missing", Missing);

    /// <summary>New concepts.</summary>
    public ObservableCollection<ConceptCardViewModel> NewConcepts { get; } = [];

    /// <summary>"New (14)".</summary>
    [ObservableProperty]
    public partial string NewHeader { get; set; } = string.Empty;

    /// <summary>Open card.</summary>
    [ObservableProperty]
    public partial ConceptCardViewModel? Selected { get; set; }

    /// <summary>Approved concepts (filtered by the search).</summary>
    public ObservableCollection<ConceptCardViewModel> Approved { get; } = [];

    /// <summary>"Approved (38)".</summary>
    [ObservableProperty]
    public partial string ApprovedHeader { get; set; } = string.Empty;

    /// <summary>Search in the approved list.</summary>
    [ObservableProperty]
    public partial string ApprovedSearch { get; set; } = string.Empty;

    /// <inheritdoc />
    public override async Task ActivateAsync()
    {
        if (!_attached)
        {
            _attached = true;
            events.Changed += async (sender, _) =>
            {
                if (!ReferenceEquals(sender, this))
                {
                    await SafeAsync(ReloadAsync).ConfigureAwait(true);
                }
            };
        }

        if (!_pickerAttached)
        {
            _pickerAttached = true;
            Picker.Changed += (_, _) =>
            {
                if (!_loading)
                {
                    _ = SafeAsync(EstimateAsync);
                }
            };
        }

        await LoadModelsAsync().ConfigureAwait(true);
        await ReloadAsync().ConfigureAwait(true);
    }

    /// <inheritdoc />
    public override async Task ProfileChangedAsync()
    {
        Missing = 0;
        LastCost = string.Empty;

        // Each profile keeps its own last choices: the new profile's are shown.
        await LoadModelsAsync().ConfigureAwait(true);
        await ReloadAsync().ConfigureAwait(true);
    }

    /// <summary>Generates.</summary>
    [RelayCommand]
    public Task GenerateAsync() => RunAsync((int)Math.Clamp(Count ?? 0, ConceptLimits.CountMin, ConceptLimits.CountMax));

    /// <summary>Generates as many as are missing.</summary>
    [RelayCommand]
    public Task FillMissingAsync() => RunAsync(Missing);

    /// <summary>Stops after the running batch (finished batches stay).</summary>
    [RelayCommand]
    public void Cancel() => _cancel?.Cancel();

    /// <summary>Approves a concept (✓, key A).</summary>
    [RelayCommand]
    public async Task ApproveAsync(ConceptCardViewModel? card)
    {
        card ??= Selected;
        if (card is null)
        {
            return;
        }

        await concepts.SetStatusAsync([card.Id], ConceptStatus.Approved, CancellationToken.None).ConfigureAwait(true);
        Remove(card);
        card.Expanded = false;
        _approved.Insert(0, card);
        Filter();
        events.Raise(this);
    }

    /// <summary>Moves a concept to the trash (✗, key D).</summary>
    [RelayCommand]
    public async Task TrashAsync(ConceptCardViewModel? card)
    {
        card ??= Selected;
        if (card is null)
        {
            return;
        }

        await concepts.SetStatusAsync([card.Id], ConceptStatus.Trashed, CancellationToken.None).ConfigureAwait(true);
        Remove(card);
        events.Raise(this);
    }

    partial void OnSelectedChanged(ConceptCardViewModel? oldValue, ConceptCardViewModel? newValue)
    {
        oldValue?.Expanded = false;
        if (newValue is not null)
        {
            newValue.Expanded = true;
            _ = SafeAsync(() => LoadContentAsync(newValue));
        }
    }

    partial void OnCountChanged(decimal? value) => _ = SafeAsync(EstimateAsync);

    partial void OnExtraPromptChanged(string value) => _ = SafeAsync(EstimateAsync);

    partial void OnApprovedSearchChanged(string value) => Filter();

    partial void OnBusyChanged(bool value)
    {
        Working = value;
        OnPropertyChanged(nameof(HasMissing));
    }

    /// <inheritdoc />
    public override void CancelWork() => Cancel();

    private async Task RunAsync(int count)
    {
        if (Busy || count < 1)
        {
            return;
        }

        if (session.Profile is not { } profile)
        {
            Status = text["concepts.no_profile"];
            return;
        }

        // The choices are made ready (license, a key for an online provider, installs of a local embedding model) and saved for the
        // profile; nothing is paid for before this passes.
        var needed = new List<string> { PipelineSteps.Concepts };
        if (Picker.Model(PipelineSteps.Embedding) is not null)
        {
            needed.Add(PipelineSteps.Embedding);
        }

        if (!await Picker.PrepareAsync(needed, "install.purpose.concepts", CancellationToken.None).ConfigureAwait(true))
        {
            Status = Picker.Status;
            return;
        }

        ResolvedModel resolved;
        ResolvedModel? embedding = null;
        try
        {
            resolved = await resolver.ResolveAsync(PipelineSteps.Concepts, Picker.Choice(PipelineSteps.Concepts), CancellationToken.None).ConfigureAwait(true);
            if (Picker.Model(PipelineSteps.Embedding) is not null)
            {
                embedding = await resolver.ResolveAsync(PipelineSteps.Embedding, Picker.Choice(PipelineSteps.Embedding), CancellationToken.None)
                    .ConfigureAwait(true);
            }
        }
        catch (ModelNotReadyException ex)
        {
            logger.LogWarning("Concept generation not started: the {Step} step has no {Missing}", ex.Step, ex.Missing);
            Status = text["concepts.no_model"];
            return;
        }

        Status = string.Empty;
        Missing = 0;
        Busy = true;
        Progress = text.Format("concepts.progress", 0, ConceptGenerator.Batches(count).Count());
        using var cancel = new CancellationTokenSource();
        _cancel = cancel;
        try
        {
            var progress = new Progress<ConceptBatch>(OnBatch);
            var result = await generator.GenerateAsync(new ConceptRequest(profile, count, ExtraPrompt.Trim(), resolved, LocalCheck, embedding), progress, cancel.Token)
                .ConfigureAwait(true);
            Report(result);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Concept generation failed");
            activity.Add(ActivityLevel.Error, text.Format("concepts.failed", ex.Message));
        }
        finally
        {
            _cancel = null;
            Busy = false;
            Progress = string.Empty;
            await ReloadAsync().ConfigureAwait(true);
        }
    }

    private void OnBatch(ConceptBatch batch)
    {
        Progress = text.Format("concepts.progress", batch.Index, batch.Batches);
        foreach (var concept in batch.Accepted.Where(c => NewConcepts.All(n => n.Id != c.Id)))
        {
            NewConcepts.Add(new ConceptCardViewModel(concept, text));
        }

        NewHeader = text.Format("concepts.new", NewConcepts.Count);
    }

    private void Report(ConceptRunResult result)
    {
        var cost = text.Usd(result.Run.ActualUsd);
        LastCost = text.Format("concepts.last_cost", cost);
        if (result.RejectedDuplicates > 0)
        {
            activity.Add(ActivityLevel.Info, text.Format("concepts.duplicates_dropped", result.RejectedDuplicates));
        }

        if (result.SemanticSkipped > 0)
        {
            activity.Add(ActivityLevel.Warning, text.Format("concepts.semantic.failed", result.SemanticSkipped));
        }

        switch (result.ErrorCode)
        {
            case "":
                activity.Add(ActivityLevel.Info, text.Format("concepts.done", result.Accepted.Count, cost));
                notifier.Notify(new AppNotification(NotificationKind.Done, text["concepts.ready.title"], text.Format("concepts.done", result.Accepted.Count, cost)));
                break;
            case "canceled":
                activity.Add(ActivityLevel.Warning, text.Format("concepts.canceled", result.Accepted.Count, cost));
                break;
            default:
                var reason = result.ErrorCode == "parse" ? text["concepts.parse_failed"] : ProviderErrors.Text(text, result.ErrorCode);
                activity.Add(ActivityLevel.Error, text.Format("concepts.error", reason, result.Accepted.Count, cost));
                notifier.Notify(new AppNotification(NotificationKind.Failed, text["concepts.error.title"], reason));
                break;
        }

        Missing = result.ErrorCode == "canceled" ? 0 : result.Missing;
    }

    private async Task LoadModelsAsync()
    {
        _loading = true;
        try
        {
            var profile = session.Profile;
            var language = profile?.ContentLanguage ?? (text.Language == "tr" ? Core.Profiles.ContentLanguages.Turkish : Core.Profiles.ContentLanguages.English);
            await Picker.LoadAsync(profile?.Id, language, PipelineSteps.ConceptPage).ConfigureAwait(true);
            PricedOn = PriceInfo.Text(catalog, text);
        }
        finally
        {
            _loading = false;
        }
    }

    /// <summary>
    /// Reads both lists first and rebuilds them without an await in between (a reload and a batch arriving at the same time cannot add a
    /// card twice). Cards that are still listed keep their object, so the open card stays open with its text (a video finishing in the
    /// background reloads this page).
    /// </summary>
    private async Task ReloadAsync()
    {
        IReadOnlyList<Concept> fresh = [];
        IReadOnlyList<Concept> approved = [];
        var profile = session.Profile;
        if (profile is not null)
        {
            fresh = await concepts.ListAsync(profile.Id, [ConceptStatus.New], false, CancellationToken.None).ConfigureAwait(true);
            approved = await concepts.ListAsync(profile.Id, [ConceptStatus.Approved], false, CancellationToken.None).ConfigureAwait(true);
            if (session.Profile?.Id != profile.Id)
            {
                return;
            }
        }

        var cards = NewConcepts.Concat(_approved).GroupBy(c => c.Id).ToDictionary(g => g.Key, g => g.First());
        ConceptCardViewModel Card(Concept concept) => cards.TryGetValue(concept.Id, out var card) ? card : new ConceptCardViewModel(concept, text);
        var selected = Selected?.Id;
        CollectionSync.Apply(NewConcepts, fresh.DistinctBy(c => c.Id).Select(Card).ToList());
        _approved.Clear();
        _approved.AddRange(approved.DistinctBy(c => c.Id).Select(Card));
        var again = NewConcepts.FirstOrDefault(c => c.Id == selected);
        if (!ReferenceEquals(again, Selected))
        {
            Selected = again;
        }
        NewHeader = text.Format("concepts.new", NewConcepts.Count);
        Filter();
        await EstimateAsync().ConfigureAwait(true);
    }

    private async Task EstimateAsync()
    {
        // Two different reasons, two different messages (0.1.5 said "choose a model" also when no profile was selected).
        if (session.Profile is not { } profile)
        {
            Estimate = text["concepts.estimate.no_profile"];
            return;
        }

        if (Picker.Model(PipelineSteps.Concepts) is not { } model)
        {
            Estimate = text["concepts.estimate.none"];
            return;
        }

        var count = (int)Math.Clamp(Count ?? 0, ConceptLimits.CountMin, ConceptLimits.CountMax);
        var estimate = await generator.EstimateAsync(profile, count, ExtraPrompt, model, CancellationToken.None).ConfigureAwait(true);
        var backfill = 0;
        if (Picker.Model(PipelineSteps.Embedding) is { } embedding)
        {
            var meaning = await generator.EstimateEmbeddingAsync(profile, embedding, count, CancellationToken.None).ConfigureAwait(true);
            estimate = estimate.Plus(meaning.Estimate);
            backfill = meaning.Backfill;
        }

        Estimate = text.Format("concepts.estimate", model.Name, count, text.Usd(estimate.MinUsd), text.Usd(estimate.MaxUsd),
            Thousands(estimate.InputTokens), Thousands(estimate.OutputTokensMax))
            + (backfill > 0 ? "\n" + text.Format("concepts.estimate.backfill", backfill) : string.Empty);
    }

    private async Task LoadContentAsync(ConceptCardViewModel card)
    {
        if (card.Lines.Count > 0)
        {
            return;
        }

        var full = await concepts.GetAsync(card.Id, CancellationToken.None).ConfigureAwait(true);
        card.ShowContent(full?.Content ?? string.Empty);
    }

    private void Remove(ConceptCardViewModel card)
    {
        var index = NewConcepts.IndexOf(card);
        NewConcepts.Remove(card);
        NewHeader = text.Format("concepts.new", NewConcepts.Count);
        if (ReferenceEquals(Selected, card) || Selected is null)
        {
            Selected = NewConcepts.Count == 0 ? null : NewConcepts[Math.Clamp(index, 0, NewConcepts.Count - 1)];
        }
    }

    private void Filter()
    {
        var search = TurkishFold.Fold(ApprovedSearch);
        CollectionSync.Apply(Approved, _approved.Where(c => search.Length == 0 || TurkishFold.Fold(c.Title).Contains(search, StringComparison.Ordinal)).ToList());

        ApprovedHeader = text.Format("concepts.approved", _approved.Count);
    }

    private string Thousands(long tokens) => tokens < 1000 ? tokens.ToString(text.Culture) : text.Format("concepts.thousand", Math.Round(tokens / 1000.0));

    private async Task SafeAsync(Func<Task> work)
    {
        try
        {
            await work().ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Concept page update failed");
            Status = text.Format("error.generic", ex.Message);
        }
    }
}
