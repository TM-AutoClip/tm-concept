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
/// 💡 Generate concepts: count (1–100), extra request (with a marker drop-down), the focus drop-downs of the channel's category (period,
/// region, tone… as data; they go into the prompt as its FOCUS line), "Suggest topics" (a cheap chat model of the topics step brings about
/// ten topics; a click adds one to the extra request), the concept model (with its effort) and the optional meaning check (local or
/// online embedding) chosen right here — the selected profile's last choices are shown, a new profile starts with none; the free
/// word-similarity check (a different check: it compares the words of title and summary, no model). The estimate is recalculated when an input or a model changes; after "Generate" the same box shows the exact cost.
/// Generate first makes the choices ready (license, an API key asked for in a dialog when the provider has none, installs of a local model)
/// and saves them for the profile; anything missing stops it before anything is paid for. Concepts arrive batch by batch; a card shows
/// title and summary, opening it shows the full text; ✓ (A) approves, ✗ (D) moves to the trash. Thrown-away duplicates and the concepts a
/// cut-off answer did not reach are not replaced automatically: a "Fill the missing" button appears, and it asks first with the estimate
/// (nothing more is paid for without a yes). A cut-off answer is a warning, not a failure: the complete concepts before the cut are kept.
/// A running call is followed step by step (getting ready, sent, thinking with the seconds, writing with about how many tokens and how many
/// concepts are complete, answer received), on the page and on the strip at the bottom of the window, so a long wait does not look like a
/// hang; Cancel stays next to it. "Suggest topics" shows the same steps under its button, a spinner and Cancel, and its result in a card
/// right below the extra request (the topics to click, why none came, a failure in red); the profile cannot be switched meanwhile.
/// </summary>
public sealed partial class ConceptsViewModel(AppSession session, IConceptStore concepts, ConceptGenerator generator, StepModelResolver resolver,
    IModelCatalog catalog, ConceptEvents events, INotifier notifier, IActivityLog activity, Localizer text, StepPickerViewModel picker,
    IFocusSource focus, TopicSuggester topics, Dialogs.IConfirmDialog confirm, Core.Costs.ICostCalculator costs, Work.WorkStatus work,
    ILogger<ConceptsViewModel> logger) : PageViewModelBase
{
    private readonly List<ConceptCardViewModel> _approved = [];
    private readonly object _topicWork = new();
    private ConceptCallProgress? _live;
    private bool _callAttached;
    private CancellationTokenSource? _cancel;
    private CancellationTokenSource? _topicCancel;
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

    /// <summary>The markers of the extra request's drop-down (sent to the concept model with their meaning).</summary>
    public IReadOnlyList<MarkerOption> PromptMarkers => MarkerOptions.For(Core.Markers.MarkerKind.Prompt, text);

    /// <summary>The categories of the focus drop-downs ("General" first; a profile made from a template starts with its category).</summary>
    public ObservableCollection<ChoiceOption> FocusCategories { get; } = [];

    /// <summary>The chosen category.</summary>
    [ObservableProperty]
    public partial ChoiceOption? FocusCategory { get; set; }

    /// <summary>The focus drop-downs: the chosen category's, then the general ones (tone, audience, structure).</summary>
    public ObservableCollection<FocusRowViewModel> FocusRows { get; } = [];

    /// <summary>The topics "Suggest topics" brought (a click adds one to the extra request).</summary>
    public ObservableCollection<string> Topics { get; } = [];

    /// <summary>The estimate of one "Suggest topics" press with the topics step's model (or why there is none).</summary>
    [ObservableProperty]
    public partial string TopicEstimate { get; set; } = string.Empty;

    /// <summary>What the last "Suggest topics" did (cost, or why it failed).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TopicCardShown))]
    public partial string TopicStatus { get; set; } = string.Empty;

    /// <summary>The last "Suggest topics" failed or brought nothing (its line is red).</summary>
    [ObservableProperty]
    public partial bool TopicFailed { get; set; }

    /// <summary>"Topic suggestions (10)".</summary>
    [ObservableProperty]
    public partial string TopicsHeader { get; set; } = string.Empty;

    /// <summary>Whether the topic card is shown (a request runs, or it brought topics or a result line).</summary>
    public bool TopicCardShown => Suggesting || Topics.Count > 0 || TopicStatus.Length > 0;

    /// <summary>Whether there are topics to click.</summary>
    public bool HasTopics => Topics.Count > 0;

    /// <summary>The steps of the running "Suggest topics" call.</summary>
    public CallStatusViewModel TopicCall { get; } = new(text);

    /// <summary>The steps of the running concept batch's call.</summary>
    public CallStatusViewModel Call { get; } = new(text);

    /// <summary>Topics are being asked for.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSuggest), nameof(TopicCardShown))]
    public partial bool Suggesting { get; set; }

    /// <summary>Whether "Suggest topics" can be pressed.</summary>
    public bool CanSuggest => !Suggesting && !Busy;

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
    [NotifyPropertyChangedFor(nameof(Idle), nameof(CanSuggest))]
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
        if (FocusCategories.Count == 0)
        {
            LoadFocus();
        }

        await ReloadAsync().ConfigureAwait(true);
    }

    /// <inheritdoc />
    public override async Task ProfileChangedAsync()
    {
        Missing = 0;
        LastCost = string.Empty;
        Topics.Clear();
        TopicStatus = string.Empty;
        TopicFailed = false;
        OnPropertyChanged(nameof(TopicCardShown));
        OnPropertyChanged(nameof(HasTopics));
        LoadFocus();

        // Each profile keeps its own last choices: the new profile's are shown.
        await LoadModelsAsync().ConfigureAwait(true);
        await ReloadAsync().ConfigureAwait(true);
    }

    /// <summary>Generates.</summary>
    [RelayCommand]
    public Task GenerateAsync() => RunAsync((int)Math.Clamp(Count ?? 0, ConceptLimits.CountMin, ConceptLimits.CountMax));

    /// <summary>
    /// Generates as many as are missing, after a confirmation that shows the estimate (the developer's rule: no money is spent again without
    /// asking). Without a concept model the run itself says what is missing, before anything is paid for.
    /// </summary>
    [RelayCommand]
    public async Task FillMissingAsync()
    {
        var missing = Missing;
        if (Busy || missing < 1)
        {
            return;
        }

        if (session.Profile is { } profile && Picker.Model(PipelineSteps.Concepts) is { } model)
        {
            var estimate = await generator.EstimateAsync(profile, missing, ExtraPrompt, Focus(profile.ContentLanguage), model, ConceptEffort(),
                CancellationToken.None).ConfigureAwait(true);
            var answer = await confirm.AskAsync(text["concepts.fill_missing.title"],
                text.Format("concepts.fill_missing.message", missing, model.Name, text.Usd(estimate.MinUsd), text.Usd(estimate.MaxUsd)),
                text.Format("concepts.fill_missing", missing), danger: false).ConfigureAwait(true);
            if (!answer.Confirmed)
            {
                return;
            }
        }

        await RunAsync(missing).ConfigureAwait(true);
    }

    /// <summary>Stops the running request at once (the answer stream is closed); the batches saved before stay.</summary>
    [RelayCommand]
    public void Cancel() => _cancel?.Cancel();

    /// <summary>Stops a running "Suggest topics" request.</summary>
    [RelayCommand]
    public void CancelTopics() => _topicCancel?.Cancel();

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
        Working = value || Suggesting;
        OnPropertyChanged(nameof(HasMissing));
    }

    partial void OnSuggestingChanged(bool value) => Working = value || Busy;

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

        AttachCalls();

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
        _live = null;
        Call.Begin(resolved.Model.Name);
        using var cancel = new CancellationTokenSource();
        _cancel = cancel;
        try
        {
            var progress = new Progress<ConceptBatch>(OnBatch);
            var live = new Progress<ConceptCallProgress>(OnLive);
            var result = await generator.GenerateAsync(new ConceptRequest(profile, count, ExtraPrompt.Trim(), resolved, LocalCheck, embedding,
                Focus(profile.ContentLanguage)), progress, live, cancel.Token).ConfigureAwait(true);
            Report(result);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Concept generation failed");
            activity.Add(ActivityLevel.Error, text.Format("concepts.failed", ex.Message));
            Status = text.Format("concepts.failed", ex.Message);
            notifier.Notify(new AppNotification(NotificationKind.Failed, text["concepts.error.title"], Status));
        }
        finally
        {
            _cancel = null;
            _live = null;
            Call.Stop();
            Busy = false;
            Progress = string.Empty;
            work.Clear(this);
            await ReloadAsync().ConfigureAwait(true);
        }
    }

    /// <summary>The page follows its two calls: every new step of one is shown on the page and on the strip.</summary>
    private void AttachCalls()
    {
        if (_callAttached)
        {
            return;
        }

        _callAttached = true;
        Call.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(CallStatusViewModel.Text) && Busy)
            {
                ShowProgress();
            }
        };
        TopicCall.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(CallStatusViewModel.Text) && TopicCall.Active)
            {
                work.Show(_topicWork, text.Format("work.topics", TopicCall.Text));
            }
        };
    }

    private void OnLive(ConceptCallProgress live)
    {
        _live = live;
        Call.Report(live.Call);
        ShowProgress();
    }

    /// <summary>"Batch 2/5 · Claude Sonnet 5.5 is writing (~3,100 tokens) · concept 2/5 ready" (the batch only when there are several).</summary>
    private void ShowProgress()
    {
        if (!Busy)
        {
            return;
        }

        var parts = new List<string>();
        if (_live is { Batches: > 1 } batches)
        {
            parts.Add(text.Format("concepts.progress", batches.Batch, batches.Batches));
        }

        if (Call.Text.Length > 0)
        {
            parts.Add(Call.Text);
        }

        if (_live is { Call.Phase: Core.Chat.ChatPhase.Writing, Ready: > 0 } writing)
        {
            parts.Add(text.Format("concepts.ready", writing.Ready, writing.Size));
        }
        else if (_live is { Call.Phase: Core.Chat.ChatPhase.Done })
        {
            parts.Add(text["concepts.saving"]);
        }

        Progress = string.Join(" · ", parts);
        work.Show(this, text.Format("work.concepts", Progress));
    }

    private void OnBatch(ConceptBatch batch)
    {
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
            Status = text.Format("concepts.semantic.failed", result.SemanticSkipped);
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
            case "truncated":
                // The model reached its answer limit: the complete concepts before the cut were saved; a warning, not a failure (and only a
                // failure when none was saved).
                var cut = text.Format("concepts.cut", result.Accepted.Count, result.Missing, cost);
                activity.Add(result.Accepted.Count > 0 ? ActivityLevel.Warning : ActivityLevel.Error, cut);
                Status = cut;
                notifier.Notify(new AppNotification(result.Accepted.Count > 0 ? NotificationKind.Warning : NotificationKind.Failed, text["concepts.cut.title"], cut));
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

        await TopicEstimateAsync(profile).ConfigureAwait(true);
        if (Picker.Model(PipelineSteps.Concepts) is not { } model)
        {
            Estimate = text["concepts.estimate.none"];
            return;
        }

        var count = (int)Math.Clamp(Count ?? 0, ConceptLimits.CountMin, ConceptLimits.CountMax);
        var estimate = await generator.EstimateAsync(profile, count, ExtraPrompt, Focus(profile.ContentLanguage), model, ConceptEffort(), CancellationToken.None)
            .ConfigureAwait(true);
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

    /// <summary>
    /// "Suggest topics": the topics step's model is made ready like any process (key, saved for the profile), then ONE call brings about ten
    /// topics in the interface language. While it runs the status says so and it can be canceled; the cost is shown here and counted in the
    /// month's spending; any failure is shown and logged (a cut-off answer still shows the complete topics before the cut).
    /// </summary>
    [RelayCommand]
    public async Task SuggestTopicsAsync()
    {
        if (!CanSuggest || session.Profile is not { } profile)
        {
            return;
        }

        if (Picker.Model(PipelineSteps.Topics) is null)
        {
            TopicStatus = text["topics.no_model"];
            return;
        }

        AttachCalls();
        TopicFailed = false;
        TopicStatus = string.Empty;
        Suggesting = true;
        TopicCall.Preparing();
        using var cancel = new CancellationTokenSource();
        _topicCancel = cancel;
        CatalogModel? paid = null;
        try
        {
            if (!await Picker.PrepareAsync([PipelineSteps.Topics], "install.purpose.topics", CancellationToken.None).ConfigureAwait(true))
            {
                TopicFailed = true;
                TopicStatus = Picker.Status;
                return;
            }

            cancel.Token.ThrowIfCancellationRequested();
            var model = await resolver.ResolveAsync(PipelineSteps.Topics, Picker.Choice(PipelineSteps.Topics), CancellationToken.None).ConfigureAwait(true);
            paid = model.Model;
            TopicCall.Begin(model.Model.Name);
            var result = await topics.SuggestAsync(profile, model, CategoryName(), Focus(text.Language), ExtraPrompt, text.Language,
                new Progress<Core.Chat.ChatProgress>(TopicCall.Report), cancel.Token).ConfigureAwait(true);
            Topics.Clear();
            foreach (var topic in result.Topics)
            {
                Topics.Add(topic);
            }

            TopicFailed = result.Topics.Count == 0;
            TopicStatus = result.Topics.Count == 0
                ? text.Format("topics.none", text.Usd(result.CostUsd))
                : text.Format(result.Cut ? "topics.done_cut" : "topics.done", result.Topics.Count, text.Usd(result.CostUsd));
            activity.Add(result.Cut || result.Topics.Count == 0 ? ActivityLevel.Warning : ActivityLevel.Info, TopicStatus);
        }
        catch (ModelNotReadyException ex)
        {
            logger.LogWarning("Topic suggestions not started: the {Step} step has no {Missing}", ex.Step, ex.Missing);
            TopicFailed = true;
            TopicStatus = text["topics.no_model"];
        }
        catch (Core.Providers.ProviderException ex)
        {
            // What a failed call was billed is said too (also on the strip).
            TopicFailed = true;
            var spent = ex.ChargedUsage is { } charged && paid is not null ? costs.Exact(paid, charged) : 0;
            TopicStatus = text.Format("topics.failed", ProviderErrors.Text(text, ex.Code)) + " · " + text.Format("cost.exact", text.Usd(spent));
            activity.Add(ActivityLevel.Error, TopicStatus);
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("Topic suggestions canceled");
            TopicStatus = text["topics.canceled"];
            activity.Add(ActivityLevel.Warning, TopicStatus);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Topic suggestions failed");
            TopicFailed = true;
            TopicStatus = text.Format("topics.failed", ex.Message);
            activity.Add(ActivityLevel.Error, TopicStatus);
        }
        finally
        {
            _topicCancel = null;
            TopicCall.Stop();
            work.Clear(_topicWork);
            Suggesting = false;
            TopicsHeader = text.Format("topics.title", Topics.Count);
            OnPropertyChanged(nameof(TopicCardShown));
            OnPropertyChanged(nameof(HasTopics));
        }
    }

    /// <summary>Adds a suggested topic to the extra request (on a line of its own) and takes it off the list.</summary>
    [RelayCommand]
    public void AddTopic(string? topic)
    {
        if (string.IsNullOrWhiteSpace(topic))
        {
            return;
        }

        var current = ExtraPrompt.TrimEnd();
        ExtraPrompt = current.Length == 0 ? topic : current + "\n" + topic;
        Topics.Remove(topic);
        TopicsHeader = text.Format("topics.title", Topics.Count);
        OnPropertyChanged(nameof(TopicCardShown));
        OnPropertyChanged(nameof(HasTopics));
    }

    /// <summary>The focus drop-downs: the categories, the profile's category chosen, its rows and the general ones.</summary>
    private void LoadFocus()
    {
        FocusCategories.Clear();
        foreach (var category in focus.Categories)
        {
            FocusCategories.Add(new ChoiceOption(category.Id, Name(category.Name)));
        }

        var start = focus.ForTemplate(session.Profile?.TemplateId)?.Id ?? FocusCatalogFile.GeneralId;
        FocusCategory = FocusCategories.FirstOrDefault(c => c.Value == start) ?? FocusCategories.FirstOrDefault();
        BuildFocusRows();
    }

    partial void OnFocusCategoryChanged(ChoiceOption? value) => BuildFocusRows();

    private void BuildFocusRows()
    {
        foreach (var row in FocusRows)
        {
            row.PropertyChanged -= OnFocusRowChanged;
        }

        FocusRows.Clear();
        var chosen = focus.Categories.FirstOrDefault(c => c.Id == FocusCategory?.Value);
        var general = focus.Categories.FirstOrDefault(c => c.Id == FocusCatalogFile.GeneralId);
        foreach (var dimension in new[] { chosen, chosen == general ? null : general }.OfType<FocusCategory>().SelectMany(c => c.Dimensions))
        {
            var options = new List<ChoiceOption> { new(string.Empty, text["focus.none"]) };
            options.AddRange(dimension.Options.Select(o => new ChoiceOption(o.Id, Name(o.Name))));
            var row = new FocusRowViewModel(dimension, Name(dimension.Name), options);
            row.PropertyChanged += OnFocusRowChanged;
            FocusRows.Add(row);
        }

        _ = SafeAsync(EstimateAsync);
    }

    private void OnFocusRowChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => _ = SafeAsync(EstimateAsync);

    /// <summary>The chosen focus as the prompt's line in <paramref name="language"/> (empty when nothing is chosen).</summary>
    private string Focus(string language) =>
        FocusText.Build(language, FocusRows.Where(r => r.Chosen is not null).Select(r => (r.Dimension, r.Chosen!)));

    /// <summary>The effort chosen for the concept model now (empty = automatic).</summary>
    private string ConceptEffort() => Picker.Row(PipelineSteps.Concepts)?.Effort?.Value ?? string.Empty;

    private string CategoryName() => FocusCategory is { Value: not FocusCatalogFile.GeneralId } category ? category.Label : string.Empty;

    private string Name(Core.Templates.LocalizedText name) => text.Language == Core.Profiles.ContentLanguages.Turkish ? name.Tr : name.En;

    private async Task TopicEstimateAsync(Core.Profiles.Profile profile)
    {
        if (Picker.Model(PipelineSteps.Topics) is not { } model)
        {
            TopicEstimate = text["topics.estimate.none"];
            return;
        }

        var estimate = await topics.EstimateAsync(profile, model, CategoryName(), Focus(text.Language), ExtraPrompt, text.Language, CancellationToken.None)
            .ConfigureAwait(true);
        TopicEstimate = text.Format("topics.estimate", model.Name, text.Usd(estimate.MinUsd), text.Usd(estimate.MaxUsd));
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
