using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using TMConcept.Core.Activity;
using TMConcept.Core.Concepts;
using TMConcept.Core.Models;
using TMConcept.Core.Pipeline;
using TMConcept.Core.Profiles;
using TMConcept.Core.Scenes;
using TMConcept.Core.Text;
using TMConcept.Desktop.Concepts;
using TMConcept.Desktop.Dialogs;
using TMConcept.Desktop.Localization;
using TMConcept.Desktop.Startup;
using TMConcept.Desktop.Work;

namespace TMConcept.Desktop.ViewModels;

/// <summary>
/// The detail panel of a concept (0.1.13, the developer: "a click must not open it under its row; a special area elsewhere on the
/// screen, with the editing there"), one per page that lists concepts. Viewing: title, number, status, words and length, the full text,
/// and the actions for its status: a new one can be approved, an approved one taken back; both can be edited by hand or with AI and
/// deleted (with a reason: the topic, the telling, or just delete). A queued or produced concept is read-only, with the note where to
/// change it. Editing: title, summary and text, placeholders at the cursor, Save / Cancel. AI edit: "What should change?", the estimate,
/// Send; ONE call with the concept step's model and effort of the host page's picker (<see cref="Attach"/>); the result opens in the
/// editor and changes nothing before Save; a failed, cut or canceled call says why and what it cost. Unsaved changes are never thrown
/// away without asking (<see cref="LeaveAsync"/>); approving or deleting the concept being edited closes the editor and cancels a running
/// AI edit. Saving a new title or summary drops its meaning-check vectors and runs the word check against the profile's other concepts:
/// a look-alike is a yellow note, not a block.
/// </summary>
public sealed partial class ConceptDetailViewModel(AppSession session, IConceptStore concepts, ConceptActions actions, ConceptEditor editor,
    StepModelResolver resolver, ConceptEvents events, IActivityLog activity, IConfirmDialog confirm, WorkStatus work, SpendingMeter spending,
    Localizer text, ILogger<ConceptDetailViewModel> logger) : ObservableObject
{
    private StepPickerViewModel? _picker;
    private object _owner = new();
    private Concept? _aiBase;
    private string _openedTitle = string.Empty;
    private string _openedSummary = string.Empty;
    private string _openedContent = string.Empty;
    private bool _editFromAi;
    private bool _aiFromEditor;
    private bool _aiEditorWasAi;
    private CancellationTokenSource? _aiCancel;
    private int _load;

    /// <summary>
    /// The instructions of the AI edits whose results the editor holds (edits can follow each other before one Save): kept as the
    /// profile's wishes only when the editor is saved, forgotten when it is canceled.
    /// </summary>
    private readonly List<string> _aiWishes = [];

    /// <summary>The panel approved, took back or trashed its concept: the host moves the card.</summary>
    public event EventHandler<ConceptStatusChange>? Changed;

    /// <summary>The concept shown (null = none).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title), nameof(Lines), nameof(Summary))]
    public partial ConceptCardViewModel? Card { get; private set; }

    /// <summary>What the panel shows.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty), nameof(IsViewing), nameof(IsEditing), nameof(IsAiEditing))]
    public partial DetailMode Mode { get; private set; }

    /// <summary>No concept is chosen.</summary>
    public bool IsEmpty => Mode == DetailMode.Empty;

    /// <summary>The text is shown with its actions.</summary>
    public bool IsViewing => Mode == DetailMode.Viewing;

    /// <summary>The editors are shown.</summary>
    public bool IsEditing => Mode == DetailMode.Editing;

    /// <summary>The AI edit box is shown.</summary>
    public bool IsAiEditing => Mode == DetailMode.AiEditing;

    /// <summary>"#12 Greek fire".</summary>
    public string Title => Card?.Title ?? string.Empty;

    /// <summary>The summary.</summary>
    public string Summary => Card?.Summary ?? string.Empty;

    /// <summary>The full text, line by line (scene lines set apart).</summary>
    public IReadOnlyList<ContentLine> Lines => Card?.Lines ?? (IReadOnlyList<ContentLine>)[];

    /// <summary>"210 words · ~1:24 · 11 Nov".</summary>
    [ObservableProperty]
    public partial string MetaLine { get; private set; } = string.Empty;

    /// <summary>"New", "Approved", "Queued" or "Produced".</summary>
    public string StatusText => Card?.Concept.Status switch
    {
        ConceptStatus.New => text["detail.status.new"],
        ConceptStatus.Approved => text["detail.status.approved"],
        ConceptStatus.Queued => text["detail.status.queued"],
        ConceptStatus.Produced => text["detail.status.produced"],
        _ => string.Empty,
    };

    /// <summary>The text can be changed (new or approved concepts only).</summary>
    public bool CanEdit => Card?.Concept.Status is ConceptStatus.New or ConceptStatus.Approved;

    /// <summary>A new concept: Approve is offered.</summary>
    public bool CanApprove => Card?.Concept.Status == ConceptStatus.New;

    /// <summary>An approved concept: Unapprove is offered.</summary>
    public bool CanUnapprove => Card?.Concept.Status == ConceptStatus.Approved;

    /// <summary>Where a read-only concept is changed (empty for an editable one).</summary>
    public string ReadOnlyNote => Card?.Concept.Status switch
    {
        ConceptStatus.Queued => text["detail.queued"],
        ConceptStatus.Produced => text["detail.produced"],
        _ => string.Empty,
    };

    /// <summary>A yellow note (the saved title looks like another concept) or why an action did not run.</summary>
    [ObservableProperty]
    public partial string Notice { get; set; } = string.Empty;

    /// <summary>The title in the editor.</summary>
    [ObservableProperty]
    public partial string EditHeadline { get; set; } = string.Empty;

    /// <summary>The summary in the editor.</summary>
    [ObservableProperty]
    public partial string EditSummary { get; set; } = string.Empty;

    /// <summary>The text in the editor.</summary>
    [ObservableProperty]
    public partial string EditText { get; set; } = string.Empty;

    /// <summary>Why the text was not saved (red).</summary>
    [ObservableProperty]
    public partial string EditStatus { get; set; } = string.Empty;

    /// <summary>A warning in the editor (yellow): the new title looks like another concept.</summary>
    [ObservableProperty]
    public partial string EditNotice { get; set; } = string.Empty;

    /// <summary>A plain line in the editor: an AI result is ready (with its cost).</summary>
    [ObservableProperty]
    public partial string EditInfo { get; set; } = string.Empty;

    /// <summary>Longest title.</summary>
    public int EditHeadlineMax => ConceptLimits.TitleHardMax;

    /// <summary>Longest summary.</summary>
    public int EditSummaryMax => ConceptLimits.SummaryMax;

    /// <summary>Longest concept text.</summary>
    public int EditMax => ConceptLimits.ContentMax;

    /// <summary>The placeholders of the editor's drop-down.</summary>
    public IReadOnlyList<MarkerOption> Placeholders => MarkerOptions.For(text);

    /// <summary>What should change (the user's wish).</summary>
    [ObservableProperty]
    public partial string AiInstruction { get; set; } = string.Empty;

    /// <summary>Longest wish.</summary>
    public int AiInstructionMax => ConceptLimits.ExtraPromptMax;

    /// <summary>The estimate of one AI edit ("≈ 0.01 $–0.02 $").</summary>
    [ObservableProperty]
    public partial string AiPrice { get; set; } = string.Empty;

    /// <summary>What the last AI edit did when it brought nothing (why, and what it cost).</summary>
    [ObservableProperty]
    public partial string AiStatus { get; set; } = string.Empty;

    /// <summary>The last AI edit failed (its line is red; a cancel is not a failure).</summary>
    [ObservableProperty]
    public partial bool AiFailed { get; set; }

    /// <summary>The AI edit call runs.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AiIdle))]
    public partial bool AiRunning { get; set; }

    /// <summary>The AI edit call does not run (Send can be pressed).</summary>
    public bool AiIdle => !AiRunning;

    /// <summary>The steps of the running AI edit call.</summary>
    public CallStatusViewModel AiCall { get; } = new(text);

    /// <summary>Whether something typed would be lost: the editor's changes (an AI result always counts), a typed wish, a running AI edit.</summary>
    public bool Unsaved => AiRunning
        || (Mode == DetailMode.AiEditing && (AiInstruction.Trim().Length > 0 || _aiFromEditor))
        || (Mode == DetailMode.Editing && (_editFromAi || !string.Equals(EditHeadline, _openedTitle, StringComparison.Ordinal)
            || !string.Equals(EditSummary, _openedSummary, StringComparison.Ordinal) || !string.Equals(EditText, _openedContent, StringComparison.Ordinal)));

    /// <summary>
    /// Uses the host page's picker (its concept step's model and effort; its key dialog) and names the host as the sender of the changes
    /// it tells the other pages.
    /// </summary>
    public void Attach(StepPickerViewModel picker, object owner)
    {
        _picker = picker;
        _owner = owner;
    }

    /// <summary>Shows <paramref name="card"/> (null = none); whatever was open is closed (ask <see cref="LeaveAsync"/> first).</summary>
    public async Task LoadAsync(ConceptCardViewModel? card)
    {
        var mine = ++_load;
        DropWork();
        Notice = string.Empty;
        Card = card;
        Mode = card is null ? DetailMode.Empty : DetailMode.Viewing;
        MetaLine = string.Empty;
        Notify();
        if (card is null)
        {
            return;
        }

        var full = await concepts.GetAsync(card.Id, CancellationToken.None).ConfigureAwait(true);
        if (mine != _load || full is null)
        {
            return;
        }

        card.Concept.Status = full.Status;
        card.ShowContent(full.Content);
        MetaLine = Meta(full);
        Notify();
    }

    /// <summary>
    /// The card's status was changed outside the panel (a row button): the panel follows it. Approving or deleting it closes the editor
    /// and cancels a running AI edit; a trashed concept leaves the panel.
    /// </summary>
    public void Follow(ConceptCardViewModel card, ConceptStatus status)
    {
        ArgumentNullException.ThrowIfNull(card);
        if (!ReferenceEquals(card, Card))
        {
            return;
        }

        DropWork();
        card.Concept.Status = status;
        if (status == ConceptStatus.Trashed)
        {
            Card = null;
            Mode = DetailMode.Empty;
        }
        else
        {
            Mode = DetailMode.Viewing;
        }

        Notify();
    }

    /// <summary>
    /// Asks before unsaved changes would be lost (another concept chosen, another tab or profile): true when nothing is lost or the user
    /// lets it go (the editor closes, a running AI edit is canceled); false when the user stays.
    /// </summary>
    public async Task<bool> LeaveAsync()
    {
        if (!Unsaved)
        {
            return true;
        }

        var answer = await confirm.AskAsync(text["detail.unsaved.title"], text["detail.unsaved"], text["detail.unsaved.discard"], danger: false,
            cancelText: text["detail.unsaved.stay"]).ConfigureAwait(true);
        if (!answer.Confirmed)
        {
            return false;
        }

        DropWork();
        if (Card is not null)
        {
            Mode = DetailMode.Viewing;
        }

        return true;
    }

    /// <summary>Stops a running AI edit (the profile changes or the window closes).</summary>
    public void CancelWork() => _aiCancel?.Cancel();

    /// <summary>Opens the text in the editor (from the AI box opened in the editor: back to the editor with its unsaved text).</summary>
    [RelayCommand]
    public async Task EditAsync()
    {
        if (Card is not { } card || !CanEdit || AiRunning || Mode == DetailMode.Editing)
        {
            return;
        }

        if (Mode == DetailMode.AiEditing && _aiFromEditor)
        {
            CancelAiEdit();
            return;
        }

        if (await concepts.GetAsync(card.Id, CancellationToken.None).ConfigureAwait(true) is not { } full)
        {
            return;
        }

        _aiBase = null;
        _aiFromEditor = false;
        _aiWishes.Clear();
        OpenEditor(full.Title, full.Summary, full.Content, string.Empty, string.Empty, fromAi: false);
    }

    /// <summary>Saves the edited text (it must still have something to narrate); the card and the panel show it at once.</summary>
    [RelayCommand]
    public async Task SaveEditAsync()
    {
        if (Card is not { } card || Mode != DetailMode.Editing)
        {
            return;
        }

        var content = (EditText ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal).Trim();
        var title = TextClean.OneLine(EditHeadline, ConceptLimits.TitleHardMax);
        var summary = TextClean.OneLine(EditSummary, ConceptLimits.SummaryMax);
        if (title.Length == 0)
        {
            EditStatus = text["concepts.edit.no_title"];
            return;
        }

        if (!SceneSplitter.Split(content).IsValid)
        {
            EditStatus = text["concepts.edit.invalid"];
            return;
        }

        // A new title or summary: the meaning check's vectors (made from them) are dropped with the save, and the word check looks for a
        // look-alike among the profile's other concepts (only a note).
        var retitled = !string.Equals(title, card.Concept.Title, StringComparison.Ordinal) || !string.Equals(summary, card.Concept.Summary, StringComparison.Ordinal);
        try
        {
            if (retitled)
            {
                await concepts.UpdateTextAsync(card.Id, title, summary, content, CancellationToken.None).ConfigureAwait(true);
            }
            else
            {
                await concepts.UpdateContentAsync(card.Id, content, CancellationToken.None).ConfigureAwait(true);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A locked database or a full disk: the editor stays open with the text, the reason is shown under it.
            logger.LogError(ex, "Saving the edited concept text failed");
            EditStatus = text.Format("error.generic", ex.Message);
            return;
        }

        card.ShowContent(content);
        card.Concept.Content = content;
        var notice = string.Empty;
        if (retitled)
        {
            card.Retitle(title, summary);
            notice = session.Profile is { } owner ? await LookAlikeAsync(owner, card, title, summary).ConfigureAwait(true) : string.Empty;
            if (notice.Length > 0)
            {
                activity.Add(ActivityLevel.Warning, notice);
            }
        }

        activity.Add(ActivityLevel.Info, text.Format("concepts.edit.saved", card.Title), text.Format("concepts.edit.saved", ActivityPlaceholders.Title));

        // The AI edits behind the saved text: their instructions become the profile's wishes for the next concepts.
        foreach (var wish in _aiWishes)
        {
            await editor.KeepWishAsync(card.Concept.ProfileId, wish).ConfigureAwait(true);
        }

        _aiWishes.Clear();
        CloseEditor();
        Mode = DetailMode.Viewing;
        Notice = notice;
        MetaLine = Meta(card.Concept);
        Notify();
        if (retitled)
        {
            // Other pages (trash, video) list titles too.
            events.Raise(_owner);
        }
    }

    /// <summary>Closes the editor without saving.</summary>
    [RelayCommand]
    public void CancelEdit()
    {
        _aiWishes.Clear();
        CloseEditor();
        if (Card is not null)
        {
            Mode = DetailMode.Viewing;
        }
    }

    /// <summary>
    /// Opens the AI edit box (from the editor: the editor's unsaved title, summary and text are what the model edits, so edits can follow
    /// each other before one Save).
    /// </summary>
    [RelayCommand]
    public async Task AiEditAsync()
    {
        if (Card is not { } card || session.Profile is null || AiRunning || Mode == DetailMode.AiEditing)
        {
            return;
        }

        if (await concepts.GetAsync(card.Id, CancellationToken.None).ConfigureAwait(true) is not { } full
            || full.Status is not (ConceptStatus.New or ConceptStatus.Approved))
        {
            // Only new and approved concepts are edited (a queued or made one already went on to its video).
            Notice = text["concepts.ai.not_editable"];
            return;
        }

        _aiFromEditor = Mode == DetailMode.Editing;
        _aiEditorWasAi = _aiFromEditor && _editFromAi;
        if (_aiFromEditor)
        {
            full.Title = TextClean.OneLine(EditHeadline, ConceptLimits.TitleHardMax);
            full.Summary = TextClean.OneLine(EditSummary, ConceptLimits.SummaryMax);
            full.Content = (EditText ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal).Trim();
        }
        else
        {
            _aiWishes.Clear();
        }

        _aiBase = full;
        CloseEditor();
        AiInstruction = string.Empty;
        AiStatus = string.Empty;
        AiFailed = false;
        Mode = DetailMode.AiEditing;
        await AiEstimateAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// Sends the AI edit: the concept model is made ready like for a run (key, saved for the profile), then ONE call. Its result opens in
    /// the editor (nothing is saved yet); a failure, a cut-off answer or a cancel keep the box open with the reason and the cost.
    /// </summary>
    [RelayCommand]
    public async Task SendAiEditAsync()
    {
        if (AiRunning || Card is not { } card || _aiBase is not { } concept || session.Profile is not { } profile || _picker is not { } picker)
        {
            return;
        }

        var instruction = TextClean.Clean(AiInstruction, ConceptLimits.ExtraPromptMax).Trim();
        if (instruction.Length == 0)
        {
            AiFailed = true;
            AiStatus = text["concepts.ai.empty"];
            return;
        }

        if (picker.Model(PipelineSteps.Concepts) is null)
        {
            AiFailed = true;
            AiStatus = text["concepts.no_model"];
            return;
        }

        AiFailed = false;
        AiStatus = string.Empty;
        AiRunning = true;
        AiCall.PropertyChanged += OnAiCall;
        AiCall.Preparing();
        using var cancel = new CancellationTokenSource();
        _aiCancel = cancel;
        try
        {
            if (!await picker.PrepareAsync([PipelineSteps.Concepts], "install.purpose.concepts", cancel.Token).ConfigureAwait(true))
            {
                AiFailed = true;
                AiStatus = picker.Status;
                return;
            }

            cancel.Token.ThrowIfCancellationRequested();
            var model = await resolver.ResolveAsync(PipelineSteps.Concepts, picker.Choice(PipelineSteps.Concepts), CancellationToken.None).ConfigureAwait(true);
            AiCall.Begin(model.Model.Name);
            var result = await editor.EditAsync(profile, concept, instruction, model, new Progress<Core.Chat.ChatProgress>(AiCall.Report), cancel.Token)
                .ConfigureAwait(true);
            var cost = text.Usd(result.CostUsd);
            if (cancel.IsCancellationRequested || session.Profile?.Id != profile.Id || !ReferenceEquals(Card, card) || Mode != DetailMode.AiEditing)
            {
                // Canceled, closed or another profile while the answer was coming: it is not opened (the run and its cost are kept).
                AiStatus = text.Format("concepts.ai.canceled", cost);
                activity.Add(ActivityLevel.Warning, AiStatus);
                return;
            }

            if (result.Concept is { } edited)
            {
                var ready = text.Format("concepts.ai.ready", cost);
                activity.Add(ActivityLevel.Info, text.Format("concepts.ai.done", card.Title, cost));
                var lookAlike = await LookAlikeAsync(profile, card, edited.Title, edited.Summary).ConfigureAwait(true);
                if (session.Profile?.Id != profile.Id || !ReferenceEquals(Card, card) || Mode != DetailMode.AiEditing)
                {
                    return;
                }

                _aiBase = null;
                _aiFromEditor = false;
                _aiWishes.Add(instruction);
                OpenEditor(edited.Title, edited.Summary, edited.Content, ready, lookAlike, fromAi: true);
                return;
            }

            if (result.ErrorCode == "canceled")
            {
                AiStatus = text.Format("concepts.ai.canceled", cost);
                activity.Add(ActivityLevel.Warning, AiStatus);
                return;
            }

            var reason = result.ErrorCode == "parse" ? text["concepts.parse_failed"] : ProviderErrors.Text(text, result.ErrorCode);
            AiFailed = true;
            AiStatus = text.Format("concepts.ai.failed", reason, cost);
            activity.Add(ActivityLevel.Error, AiStatus);
        }
        catch (ModelNotReadyException ex)
        {
            logger.LogWarning("AI edit not started: the {Step} step has no {Missing}", ex.Step, ex.Missing);
            AiFailed = true;
            AiStatus = text["concepts.no_model"];
        }
        catch (OperationCanceledException)
        {
            // Canceled before anything was sent: nothing was paid.
            AiStatus = text.Format("concepts.ai.canceled", text.Usd(0));
            activity.Add(ActivityLevel.Warning, AiStatus);
        }
        catch (Exception ex)
        {
            // A locked database or an unexpected answer: the box stays open with the reason; the concept did not change.
            logger.LogError(ex, "AI edit failed");
            AiFailed = true;
            AiStatus = text.Format("error.generic", ex.Message);
            activity.Add(ActivityLevel.Error, AiStatus);
        }
        finally
        {
            _aiCancel = null;
            AiCall.PropertyChanged -= OnAiCall;
            AiCall.Stop();
            work.Clear(AiCall);
            AiRunning = false;

            // The edit call's run is saved: the spending pill reads the month again.
            await spending.EndAsync(AiCall).ConfigureAwait(true);
        }
    }

    /// <summary>Cancel in the AI box: stops a running call (the box stays with what it cost), otherwise closes the box.</summary>
    [RelayCommand]
    public void CancelAiEdit()
    {
        if (_aiCancel is { } running)
        {
            running.Cancel();
            return;
        }

        var unsaved = _aiBase;
        var fromEditor = _aiFromEditor;
        _aiBase = null;
        _aiFromEditor = false;
        if (fromEditor && unsaved is not null && Card is not null)
        {
            // Opened from the editor: back to it with its unsaved title, summary and text (nothing typed there is lost).
            OpenEditor(unsaved.Title, unsaved.Summary, unsaved.Content, string.Empty, string.Empty, _aiEditorWasAi);
            return;
        }

        Mode = Card is null ? DetailMode.Empty : DetailMode.Viewing;
    }

    /// <summary>Approves the concept.</summary>
    [RelayCommand]
    public Task ApproveAsync() => ChangeAsync(ConceptStatus.Approved, card => actions.ApproveAsync(card.Concept, _owner));

    /// <summary>Takes the approved concept back to the new ones.</summary>
    [RelayCommand]
    public Task UnapproveAsync() => ChangeAsync(ConceptStatus.New, card => actions.UnapproveAsync(card.Concept, _owner));

    /// <summary>Deletes because the topic was not liked: new concepts and topics stay away from it.</summary>
    [RelayCommand]
    public Task TrashTopicAsync() => TrashAsync(TrashReason.Topic);

    /// <summary>Deletes because the telling was not liked: new concepts avoid that kind of telling.</summary>
    [RelayCommand]
    public Task TrashTellingAsync() => TrashAsync(TrashReason.Telling);

    /// <summary>Deletes without a reason (it still counts for the duplicate check).</summary>
    [RelayCommand]
    public Task TrashJustAsync() => TrashAsync(TrashReason.None);

    partial void OnAiInstructionChanged(string value) => _ = SafeAsync(AiEstimateAsync);

    private Task TrashAsync(TrashReason reason) => ChangeAsync(ConceptStatus.Trashed, card => actions.TrashAsync([card.Concept], reason, _owner));

    private async Task ChangeAsync(ConceptStatus status, Func<ConceptCardViewModel, Task> change)
    {
        if (Card is not { } card)
        {
            return;
        }

        try
        {
            await change(card).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "The concept's status could not be changed to {Status}", status);
            Notice = text.Format("error.generic", ex.Message);
            return;
        }

        Follow(card, status);
        Changed?.Invoke(this, new ConceptStatusChange(card, status));
    }

    /// <summary>Closes the editor and the AI box and cancels a running AI edit (nothing is saved).</summary>
    private void DropWork()
    {
        _aiCancel?.Cancel();
        _aiBase = null;
        _aiFromEditor = false;
        _aiWishes.Clear();
        AiStatus = string.Empty;
        AiFailed = false;
        CloseEditor();
    }

    private void CloseEditor()
    {
        EditNotice = string.Empty;
        EditInfo = string.Empty;
        EditStatus = string.Empty;
        _editFromAi = false;
    }

    private void OpenEditor(string title, string summary, string content, string info, string notice, bool fromAi)
    {
        EditHeadline = title;
        EditSummary = summary;
        EditText = content;
        _openedTitle = title;
        _openedSummary = summary;
        _openedContent = content;
        _editFromAi = fromAi;
        EditStatus = string.Empty;
        EditInfo = info;
        EditNotice = notice;
        Mode = DetailMode.Editing;
    }

    private void Notify()
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(Lines));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(CanEdit));
        OnPropertyChanged(nameof(CanApprove));
        OnPropertyChanged(nameof(CanUnapprove));
        OnPropertyChanged(nameof(ReadOnlyNote));
    }

    /// <summary>"210 words · ~1:24 · 11 Nov" (the words and the length of the narration lines, at the language's speaking rate).</summary>
    private string Meta(Concept concept)
    {
        var words = (concept.Content ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n')
            .Where(l => l.Trim().Length > 0 && !SceneSplitter.IsSceneLine(l))
            .Sum(l => l.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length);
        var seconds = (int)Math.Round(words / OutputLanguages.Of(concept.Language).WordsPerSecond);
        var length = TimeSpan.FromSeconds(seconds).ToString(@"m\:ss", text.Culture);
        return text.Format("detail.meta", words, length, concept.CreatedAtUtc.ToLocalTime().ToString("d MMM HH:mm", text.Culture));
    }

    /// <summary>
    /// The word check of an edited title and summary against the profile's other concepts (every status, the concept itself left out): the
    /// note of a look-alike, or empty.
    /// </summary>
    private async Task<string> LookAlikeAsync(Profile profile, ConceptCardViewModel card, string title, string summary)
    {
        var history = (await concepts.HistoryAsync(profile.Id, CancellationToken.None).ConfigureAwait(true)).Where(h => h.Id != card.Id).ToList();
        var result = Core.Duplicates.DuplicateCheck.Sift([new Core.Duplicates.DuplicateSubject(card.Id, card.Concept.Number, title, summary)], history,
            profile.ContentLanguage)[0].Result;
        return result.Kind is Core.Duplicates.DuplicateKind.Duplicate or Core.Duplicates.DuplicateKind.Similar
            ? "⚠ " + text.Format("concepts.ai.lookalike", result.OtherNumber, result.OtherTitle, Math.Round(result.Similarity * 100))
            : string.Empty;
    }

    /// <summary>The estimate of one AI edit with the concept model now chosen.</summary>
    private async Task AiEstimateAsync()
    {
        if (_aiBase is not { } concept || session.Profile is not { } profile || _picker is not { } picker)
        {
            return;
        }

        if (picker.Model(PipelineSteps.Concepts) is not { } model)
        {
            AiPrice = text["concepts.estimate.none"];
            return;
        }

        var effort = picker.Row(PipelineSteps.Concepts)?.Effort?.Value ?? string.Empty;
        var estimate = await editor.EstimateAsync(profile, concept, AiInstruction, model, effort, CancellationToken.None).ConfigureAwait(true);
        AiPrice = text.Format("concepts.ai.estimate", model.Name, text.Usd(estimate.MinUsd), text.Usd(estimate.MaxUsd));
    }

    private void OnAiCall(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CallStatusViewModel.Text) && AiCall.Active)
        {
            work.Show(AiCall, text.Format("work.ai_edit", AiCall.Text));
        }
    }

    private async Task SafeAsync(Func<Task> work)
    {
        try
        {
            await work().ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Concept detail update failed");
            Notice = text.Format("error.generic", ex.Message);
        }
    }
}
