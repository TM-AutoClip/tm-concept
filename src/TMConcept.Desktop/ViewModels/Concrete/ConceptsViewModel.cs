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
/// 💡 Generate concepts. The left panel, top to bottom: count (1–100); extra request (plain text) and a small "Suggest topics"
/// next to its title (a cheap chat model of the topics step brings about ten topics from the channel concept and the extra request so far;
/// a click adds one to the extra request); a folded "Fine-tuning (optional)" with only tone, audience and structure (nothing that asks the
/// user to know the subject: period, region or field come from the extra request; "any" by default; the choices go into the prompt as its
/// FOCUS line); the duplicate checks on one line (by words: free, no model; by meaning: a local or online embedding model picked right
/// there); a folded "Models" whose title names the concept model (opened by itself while none is chosen). Long explanations are "?" tips.
/// The selected profile's last model choices are shown, a new profile starts with none. The estimate is recalculated when an input or a
/// model changes; after "Generate" the same box shows the exact cost.
/// Generate first makes the choices ready (license, an API key asked for in a dialog when the provider has none, installs of a local model)
/// and saves them for the profile; anything missing stops it before anything is paid for. Concepts arrive batch by batch; a card shows
/// title and summary, opening it shows the full text; ✓ (A) approves, ✗ (D) moves to the trash. Thrown-away duplicates and the concepts a
/// cut-off answer did not reach are not replaced automatically: a "Fill the missing" button appears, and it asks first with the estimate
/// (nothing more is paid for without a yes). A cut-off answer is a warning, not a failure: the complete concepts before the cut are kept.
/// A running call is followed step by step (getting ready, sent, thinking with the seconds, writing with about how many tokens and how many
/// concepts are complete, answer received), on the page and on the strip at the bottom of the window, so a long wait does not look like a
/// hang; Cancel stays next to it. "Suggest topics" shows the same steps under its button, a spinner and Cancel, and its result in a card
/// right below the extra request (the topics to click, why none came, a failure in red); the profile cannot be switched meanwhile.
/// A new card (✎ when open) or an approved concept (a click in its list) opens its text in an editor in place of the list: the text can be
/// fixed and placeholders put in at the cursor from a drop-down (pauses, speed, transitions; the program applies them in the video, no
/// model sees them); saving needs a text with something to narrate.
/// Every new card shows its actions at once (10 Nov 2026: the developer did not find them on the opened card only): ✨ AI edit, ✎ edit the
/// text yourself, ✓ approve, 🗑 delete (to the trash, which still counts it for the duplicate check). ✨ (on a new card, next to an
/// approved title, or in the open editor) opens a box in place of the list: "What should change?", the estimate, Send and Cancel. Send is
/// ONE call with the concept step's model and effort (<see cref="ConceptEditor"/>), followed step by step like Suggest topics; its result
/// opens in the text editor with title and summary, so the user checks it before Save (nothing changes before). A failed, cut-off or
/// canceled call changes nothing and says why and what it cost. Saving a new title or summary drops the concept's meaning-check vectors
/// and runs the word check against the profile's other concepts: a look-alike is a yellow note, not a block.
/// </summary>
public sealed partial class ConceptsViewModel(AppSession session, IConceptStore concepts, ConceptGenerator generator, StepModelResolver resolver,
    IModelCatalog catalog, ConceptEvents events, INotifier notifier, IActivityLog activity, Localizer text, StepPickerViewModel picker,
    IFocusSource focus, TopicSuggester topics, ITopicSuggestionStore suggestions, Dialogs.IConfirmDialog confirm, Core.Costs.ICostCalculator costs, Work.WorkStatus work,
    ConceptEditor editor, ILogger<ConceptsViewModel> logger) : PageViewModelBase
{
    /// <summary>The steps of the page that are not model cards: the meaning check stands next to the word check.</summary>
    private static readonly string[] InlineSteps = [PipelineSteps.Embedding];

    private readonly List<ConceptCardViewModel> _approved = [];
    private readonly object _topicWork = new();
    private readonly object _aiWork = new();
    private Concept? _aiBase;
    private string _editOpenedTitle = string.Empty;
    private string _editOpenedSummary = string.Empty;
    private string _editOpenedContent = string.Empty;
    private bool _editFromAi;
    private bool _aiFromEditor;
    private bool _aiEditorWasAi;
    private CancellationTokenSource? _aiCancel;
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

    /// <summary>The fine-tuning drop-downs (tone, audience, structure), "any" until the user picks one.</summary>
    public ObservableCollection<FocusRowViewModel> FocusRows { get; } = [];

    /// <summary>"Fine-tuning (optional)", with what is chosen ("Fine-tuning (optional) · mysterious") so the folded title tells it.</summary>
    [ObservableProperty]
    public partial string FineTuneHeader { get; set; } = string.Empty;

    /// <summary>"Models · Claude Sonnet 5.5" (or that none is chosen yet).</summary>
    [ObservableProperty]
    public partial string ModelsHeader { get; set; } = string.Empty;

    /// <summary>Whether the folded "Models" area is open (it opens by itself while no concept model is chosen).</summary>
    [ObservableProperty]
    public partial bool ModelsOpen { get; set; }

    /// <summary>The meaning check's picker, shown on the line of the word check (null before the page is loaded).</summary>
    public StepModelRowViewModel? Meaning => Picker.Row(PipelineSteps.Embedding);

    /// <summary>The topics "Suggest topics" brought (a click adds one to the extra request).</summary>
    public ObservableCollection<string> Topics { get; } = [];

    /// <summary>The estimate of one "Suggest topics" press with the topics step's model (or why there is none).</summary>
    [ObservableProperty]
    public partial string TopicEstimate { get; set; } = string.Empty;

    /// <summary>The short price of one "Suggest topics" press, next to its button ("≈ 0.001–0.003 $"; empty without a model).</summary>
    [ObservableProperty]
    public partial string TopicPrice { get; set; } = string.Empty;

    /// <summary>What the last "Suggest topics" did (cost, or why it failed).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TopicCardShown))]
    public partial string TopicStatus { get; set; } = string.Empty;

    /// <summary>How many topics of the last answer repeated an earlier suggestion or a concept and were left out (empty when none).</summary>
    [ObservableProperty]
    public partial string TopicRepeats { get; set; } = string.Empty;

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

    /// <summary>
    /// The models of this page (the profile's last choices): the concept model and the topics model as compact cards, the meaning check on
    /// the line of the word check.
    /// </summary>
    public StepPickerViewModel Picker { get; } = Layout(picker);

    /// <summary>Local duplicate check.</summary>
    [ObservableProperty]
    public partial bool LocalCheck { get; set; } = true;

    /// <summary>Estimate.</summary>
    [ObservableProperty]
    public partial string Estimate { get; set; } = string.Empty;

    /// <summary>
    /// A warning of the last press shown right under its buttons (an answer cut off or ended early: how many were saved, how many are
    /// missing and why); errors stay in <see cref="PageViewModelBase.Status"/>, shown in red next to it.
    /// </summary>
    [ObservableProperty]
    public partial string Notice { get; set; } = string.Empty;

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

    /// <summary>The concept whose text is being edited (null = the new concepts are listed).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Editing))]
    [NotifyPropertyChangedFor(nameof(Listing))]
    public partial ConceptCardViewModel? EditCard { get; set; }

    /// <summary>Whether the editor is shown.</summary>
    public bool Editing => EditCard is not null;

    /// <summary>Whether the new concepts are listed (no editor, no AI edit box).</summary>
    public bool Listing => EditCard is null && AiEditCard is null;

    /// <summary>The title in the editor.</summary>
    [ObservableProperty]
    public partial string EditHeadline { get; set; } = string.Empty;

    /// <summary>The summary in the editor.</summary>
    [ObservableProperty]
    public partial string EditSummary { get; set; } = string.Empty;

    /// <summary>A warning in the editor (yellow): the new title looks like another concept.</summary>
    [ObservableProperty]
    public partial string EditNotice { get; set; } = string.Empty;

    /// <summary>A plain line in the editor: an AI result is ready (with its cost).</summary>
    [ObservableProperty]
    public partial string EditInfo { get; set; } = string.Empty;

    /// <summary>Longest title in the editor.</summary>
    public int EditHeadlineMax => ConceptLimits.TitleHardMax;

    /// <summary>Longest summary in the editor.</summary>
    public int EditSummaryMax => ConceptLimits.SummaryMax;

    /// <summary>The concept being edited with AI (null = no AI edit box).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AiEditing))]
    [NotifyPropertyChangedFor(nameof(Listing))]
    public partial ConceptCardViewModel? AiEditCard { get; set; }

    /// <summary>Whether the AI edit box is shown.</summary>
    public bool AiEditing => AiEditCard is not null;

    /// <summary>"AI edit · #12 Greek fire".</summary>
    [ObservableProperty]
    public partial string AiEditTitle { get; set; } = string.Empty;

    /// <summary>What should change (the user's wish).</summary>
    [ObservableProperty]
    public partial string AiInstruction { get; set; } = string.Empty;

    /// <summary>Longest wish.</summary>
    public int AiInstructionMax => ConceptLimits.ExtraPromptMax;

    /// <summary>The estimate of one AI edit with the concept model (or why there is none).</summary>
    [ObservableProperty]
    public partial string AiEditPrice { get; set; } = string.Empty;

    /// <summary>What the last AI edit did when it brought nothing (why, and what it cost).</summary>
    [ObservableProperty]
    public partial string AiEditStatus { get; set; } = string.Empty;

    /// <summary>The last AI edit failed (its line is red; a cancel is not a failure).</summary>
    [ObservableProperty]
    public partial bool AiEditFailed { get; set; }

    /// <summary>The AI edit call runs.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AiEditIdle))]
    public partial bool AiEditRunning { get; set; }

    /// <summary>The AI edit call does not run (Send can be pressed).</summary>
    public bool AiEditIdle => !AiEditRunning;

    /// <summary>The steps of the running AI edit call.</summary>
    public CallStatusViewModel AiEditCall { get; } = new(text);

    /// <summary>"Edit text · #12 Greek fire".</summary>
    [ObservableProperty]
    public partial string EditTitle { get; set; } = string.Empty;

    /// <summary>The text in the editor.</summary>
    [ObservableProperty]
    public partial string EditText { get; set; } = string.Empty;

    /// <summary>Why the text was not saved.</summary>
    [ObservableProperty]
    public partial string EditStatus { get; set; } = string.Empty;

    /// <summary>Longest concept text.</summary>
    public int EditMax => ConceptLimits.ContentMax;

    /// <summary>The placeholders of the editor's drop-down.</summary>
    public IReadOnlyList<MarkerOption> Placeholders => MarkerOptions.For(text);

    /// <summary>Opens the text of a concept (an open new card, or an approved one) in the editor.</summary>
    [RelayCommand]
    public async Task EditAsync(ConceptCardViewModel? card)
    {
        card ??= Selected;
        if (card is null || ReferenceEquals(EditCard, card) || Held(card))
        {
            return;
        }

        if (ReferenceEquals(AiEditCard, card) && _aiFromEditor)
        {
            // The AI box was opened from this concept's editor: back to the editor with its unsaved text.
            CancelAiEdit();
            return;
        }

        if (await concepts.GetAsync(card.Id, CancellationToken.None).ConfigureAwait(true) is not { } full)
        {
            return;
        }

        AiEditCard = null;
        _aiBase = null;
        _aiFromEditor = false;
        OpenEditor(card, full.Title, full.Summary, full.Content, string.Empty, string.Empty, fromAi: false);
    }

    /// <summary>Saves the edited text (it must still have something to narrate); the card shows it at once.</summary>
    [RelayCommand]
    public async Task SaveEditAsync()
    {
        if (EditCard is not { } card)
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

        if (!Core.Scenes.SceneSplitter.Split(content).IsValid)
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
        if (retitled)
        {
            card.Retitle(title, summary);
            var lookAlike = session.Profile is { } owner ? await LookAlikeAsync(owner, card, title, summary).ConfigureAwait(true) : string.Empty;
            if (lookAlike.Length > 0)
            {
                Notice = lookAlike;
                activity.Add(ActivityLevel.Warning, lookAlike);
            }
        }

        activity.Add(ActivityLevel.Info, text.Format("concepts.edit.saved", card.Title), text.Format("concepts.edit.saved", ActivityPlaceholders.Title));
        CloseEditor();
        if (retitled)
        {
            // Other pages (trash, video) list titles too.
            events.Raise(this);
        }
    }

    /// <summary>
    /// Opens the AI edit box for a new or approved concept in place of the list (from the open editor: the editor's unsaved title, summary and
    /// text are what the model edits, so edits can follow each other before one Save).
    /// </summary>
    [RelayCommand]
    public async Task AiEditAsync(ConceptCardViewModel? card)
    {
        card ??= Selected;
        if (card is null || session.Profile is null || ReferenceEquals(AiEditCard, card) || Held(card))
        {
            return;
        }

        if (await concepts.GetAsync(card.Id, CancellationToken.None).ConfigureAwait(true) is not { } full
            || full.Status is not (ConceptStatus.New or ConceptStatus.Approved))
        {
            // Only new and approved concepts are edited (a queued or made one already went on to its video).
            Status = text["concepts.ai.not_editable"];
            return;
        }

        // From the open editor: its unsaved title, summary and text are what the model edits, and Cancel in the box goes back to them.
        _aiFromEditor = ReferenceEquals(EditCard, card);
        _aiEditorWasAi = _aiFromEditor && _editFromAi;
        if (_aiFromEditor)
        {
            full.Title = TextClean.OneLine(EditHeadline, ConceptLimits.TitleHardMax);
            full.Summary = TextClean.OneLine(EditSummary, ConceptLimits.SummaryMax);
            full.Content = (EditText ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal).Trim();
        }

        _aiBase = full;
        CloseEditor();
        AiEditTitle = text.Format("concepts.ai.title", card.Title);
        AiInstruction = string.Empty;
        AiEditStatus = string.Empty;
        AiEditFailed = false;
        AiEditCard = card;
        await AiEstimateAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// Sends the AI edit: the concept model is made ready like for Generate (key, saved for the profile), then ONE call. Its result opens in
    /// the editor (nothing is saved yet); a failure, a cut-off answer or a cancel keep the box open with the reason and the cost.
    /// </summary>
    [RelayCommand]
    public async Task SendAiEditAsync()
    {
        if (AiEditRunning || AiEditCard is not { } card || _aiBase is not { } concept || session.Profile is not { } profile)
        {
            return;
        }

        var instruction = TextClean.Clean(AiInstruction, ConceptLimits.ExtraPromptMax).Trim();
        if (instruction.Length == 0)
        {
            AiEditFailed = true;
            AiEditStatus = text["concepts.ai.empty"];
            return;
        }

        if (Picker.Model(PipelineSteps.Concepts) is null)
        {
            AiEditFailed = true;
            AiEditStatus = text["concepts.no_model"];
            ModelsOpen = true;
            return;
        }

        AttachCalls();
        AiEditFailed = false;
        AiEditStatus = string.Empty;
        AiEditRunning = true;
        AiEditCall.Preparing();
        using var cancel = new CancellationTokenSource();
        _aiCancel = cancel;
        try
        {
            if (!await Picker.PrepareAsync([PipelineSteps.Concepts], "install.purpose.concepts", CancellationToken.None).ConfigureAwait(true))
            {
                AiEditFailed = true;
                AiEditStatus = Picker.Status;
                return;
            }

            cancel.Token.ThrowIfCancellationRequested();
            var model = await resolver.ResolveAsync(PipelineSteps.Concepts, Picker.Choice(PipelineSteps.Concepts), CancellationToken.None).ConfigureAwait(true);
            AiEditCall.Begin(model.Model.Name);
            var result = await editor.EditAsync(profile, concept, instruction, model, new Progress<Core.Chat.ChatProgress>(AiEditCall.Report), cancel.Token)
                .ConfigureAwait(true);
            var cost = text.Usd(result.CostUsd);
            if (cancel.IsCancellationRequested || session.Profile?.Id != profile.Id || !ReferenceEquals(AiEditCard, card))
            {
                // Canceled, closed or another profile while the answer was coming: it is not opened (the run and its cost are kept).
                AiEditStatus = text.Format("concepts.ai.canceled", cost);
                activity.Add(ActivityLevel.Warning, AiEditStatus);
                return;
            }

            if (result.Concept is { } edited)
            {
                var ready = text.Format("concepts.ai.ready", cost);
                activity.Add(ActivityLevel.Info, text.Format("concepts.ai.done", card.Title, cost));
                var lookAlike = await LookAlikeAsync(profile, card, edited.Title, edited.Summary).ConfigureAwait(true);
                if (session.Profile?.Id != profile.Id || !ReferenceEquals(AiEditCard, card))
                {
                    return;
                }

                AiEditCard = null;
                _aiBase = null;
                _aiFromEditor = false;
                OpenEditor(card, edited.Title, edited.Summary, edited.Content, ready, lookAlike, fromAi: true);
                return;
            }

            if (result.ErrorCode == "canceled")
            {
                AiEditStatus = text.Format("concepts.ai.canceled", cost);
                activity.Add(ActivityLevel.Warning, AiEditStatus);
                return;
            }

            var reason = result.ErrorCode == "parse" ? text["concepts.parse_failed"] : ProviderErrors.Text(text, result.ErrorCode);
            AiEditFailed = true;
            AiEditStatus = text.Format("concepts.ai.failed", reason, cost);
            activity.Add(ActivityLevel.Error, AiEditStatus);
        }
        catch (ModelNotReadyException ex)
        {
            logger.LogWarning("AI edit not started: the {Step} step has no {Missing}", ex.Step, ex.Missing);
            AiEditFailed = true;
            AiEditStatus = text["concepts.no_model"];
            ModelsOpen = true;
        }
        catch (OperationCanceledException)
        {
            // Canceled before anything was sent: nothing was paid.
            AiEditStatus = text.Format("concepts.ai.canceled", text.Usd(0));
            activity.Add(ActivityLevel.Warning, AiEditStatus);
        }
        catch (Exception ex)
        {
            // A locked database or an unexpected answer: the box stays open with the reason; the concept did not change.
            logger.LogError(ex, "AI edit failed");
            AiEditFailed = true;
            AiEditStatus = text.Format("error.generic", ex.Message);
            activity.Add(ActivityLevel.Error, AiEditStatus);
        }
        finally
        {
            _aiCancel = null;
            AiEditCall.Stop();
            work.Clear(_aiWork);
            AiEditRunning = false;
        }
    }

    /// <summary>Cancel in the AI edit box: stops a running call (the box stays with what it cost), otherwise closes the box.</summary>
    [RelayCommand]
    public void CancelAiEdit()
    {
        if (_aiCancel is { } running)
        {
            running.Cancel();
            return;
        }

        var back = AiEditCard;
        var unsaved = _aiBase;
        var fromEditor = _aiFromEditor;
        AiEditCard = null;
        _aiBase = null;
        _aiFromEditor = false;
        if (fromEditor && back is not null && unsaved is not null)
        {
            // Opened from the editor: back to it with its unsaved title, summary and text (nothing typed there is lost).
            OpenEditor(back, unsaved.Title, unsaved.Summary, unsaved.Content, string.Empty, string.Empty, _aiEditorWasAi);
        }
    }

    /// <summary>Closes the editor without saving.</summary>
    [RelayCommand]
    public void CancelEdit() => CloseEditor();

    /// <summary>Whether the open editor holds something not saved (an AI result always counts as unsaved).</summary>
    public bool EditUnsaved => EditCard is not null
        && (_editFromAi || !string.Equals(EditHeadline, _editOpenedTitle, StringComparison.Ordinal)
            || !string.Equals(EditSummary, _editOpenedSummary, StringComparison.Ordinal)
            || !string.Equals(EditText, _editOpenedContent, StringComparison.Ordinal));

    /// <summary>
    /// Whether opening <paramref name="card"/> (in the editor or the AI box) must wait, with the reason shown where the user looks: an AI
    /// edit runs, the AI box holds a typed wish or the editor's unsaved text for another concept, or the editor holds unsaved changes of
    /// another concept. Nothing typed is thrown away without the user's Save or Cancel.
    /// </summary>
    private bool Held(ConceptCardViewModel card)
    {
        if (AiEditRunning)
        {
            AiEditFailed = false;
            AiEditStatus = text["concepts.ai.busy"];
            return true;
        }

        if (AiEditCard is { } box && !ReferenceEquals(box, card) && (!string.IsNullOrWhiteSpace(AiInstruction) || _aiFromEditor))
        {
            AiEditFailed = false;
            AiEditStatus = text["concepts.unsaved"];
            return true;
        }

        if (EditCard is { } open && !ReferenceEquals(open, card) && EditUnsaved)
        {
            EditStatus = text["concepts.unsaved"];
            return true;
        }

        return false;
    }

    /// <summary>Closes the editor (nothing is saved).</summary>
    private void CloseEditor()
    {
        EditCard = null;
        EditNotice = string.Empty;
        EditInfo = string.Empty;
        _editFromAi = false;
    }

    /// <summary>Fills the editor (title, summary, text) for <paramref name="card"/> and shows it in place of the list.</summary>
    private void OpenEditor(ConceptCardViewModel card, string title, string summary, string content, string info, string notice, bool fromAi)
    {
        EditTitle = text.Format("concepts.edit.title", card.Title);
        EditHeadline = title;
        EditSummary = summary;
        EditText = content;
        _editOpenedTitle = title;
        _editOpenedSummary = summary;
        _editOpenedContent = content;
        _editFromAi = fromAi;
        EditStatus = string.Empty;
        EditInfo = info;
        EditNotice = notice;
        EditCard = card;
    }

    /// <summary>
    /// The word check of an edited title and summary against the profile's other concepts (every status, the concept itself left out): the
    /// note of a look-alike, or empty.
    /// </summary>
    private async Task<string> LookAlikeAsync(Core.Profiles.Profile profile, ConceptCardViewModel card, string title, string summary)
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
        if (_aiBase is not { } concept || session.Profile is not { } profile)
        {
            return;
        }

        if (Picker.Model(PipelineSteps.Concepts) is not { } model)
        {
            AiEditPrice = text["concepts.estimate.none"];
            return;
        }

        var estimate = await editor.EstimateAsync(profile, concept, AiInstruction, model, ConceptEffort(), CancellationToken.None).ConfigureAwait(true);
        AiEditPrice = text.Format("concepts.ai.estimate", model.Name, text.Usd(estimate.MinUsd), text.Usd(estimate.MaxUsd));
    }

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
                ShowModels(open: false);
                if (!_loading)
                {
                    _ = SafeAsync(EstimateAsync);
                }
            };
        }

        await LoadModelsAsync().ConfigureAwait(true);
        if (FocusRows.Count == 0)
        {
            LoadFocus();
        }

        await ReloadAsync().ConfigureAwait(true);
    }

    /// <inheritdoc />
    public override async Task ProfileChangedAsync()
    {
        // The last press's lines belong to the old profile (its "Fill the missing" button goes too).
        Status = string.Empty;
        Notice = string.Empty;
        Missing = 0;
        LastCost = string.Empty;
        // An editor or an AI edit box shows a concept of the old profile: closed (a running AI edit is canceled).
        _aiCancel?.Cancel();
        AiEditCard = null;
        _aiBase = null;
        _aiFromEditor = false;
        CloseEditor();
        Topics.Clear();
        TopicStatus = string.Empty;
        TopicRepeats = string.Empty;
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
            try
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
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Without an estimate nothing is paid for: the reason is shown and nothing starts.
                logger.LogError(ex, "The estimate for the missing concepts failed");
                Status = text.Format("error.generic", ex.Message);
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

    partial void OnAiInstructionChanged(string value) => _ = SafeAsync(AiEstimateAsync);

    partial void OnAiEditRunningChanged(bool value) => Working = value || Busy || Suggesting;

    partial void OnApprovedSearchChanged(string value) => Filter();

    partial void OnBusyChanged(bool value)
    {
        Working = value || Suggesting || AiEditRunning;
        OnPropertyChanged(nameof(HasMissing));
    }

    partial void OnSuggestingChanged(bool value) => Working = value || Busy || AiEditRunning;

    /// <inheritdoc />
    public override void CancelWork()
    {
        Cancel();
        _aiCancel?.Cancel();
    }

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
            ModelsOpen = true;
            return;
        }

        Status = string.Empty;
        Notice = string.Empty;
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
        AiEditCall.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(CallStatusViewModel.Text) && AiEditCall.Active)
            {
                work.Show(_aiWork, text.Format("work.ai_edit", AiEditCall.Text));
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
            case "truncated" or "incomplete":
                // The model reached its answer limit (or the answer ended early): the complete concepts before the cut were saved; a
                // warning under the buttons, not a failure (and only a failure when none was saved). The missing count tells apart the
                // concepts thrown away as duplicates.
                var dropped = result.RejectedDuplicates > 0 ? text.Format("concepts.missing.dropped", result.RejectedDuplicates) : string.Empty;
                var cut = text.Format(result.ErrorCode == "truncated" ? "concepts.cut" : "concepts.incomplete", result.Accepted.Count, result.Missing, cost,
                    dropped);
                activity.Add(result.Accepted.Count > 0 ? ActivityLevel.Warning : ActivityLevel.Error, cut);
                Notice = cut;
                notifier.Notify(new AppNotification(result.Accepted.Count > 0 ? NotificationKind.Warning : NotificationKind.Failed, text["concepts.cut.title"], cut));
                break;
            default:
                var reason = result.ErrorCode == "parse" ? text["concepts.parse_failed"] : ProviderErrors.Text(text, result.ErrorCode);
                var error = text.Format("concepts.error", reason, result.Accepted.Count, cost);
                activity.Add(ActivityLevel.Error, error);
                Status = error;
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
            OnPropertyChanged(nameof(Meaning));
            ShowModels(open: true);
        }
        finally
        {
            _loading = false;
        }
    }

    /// <summary>
    /// The folded "Models" title names the concept model; with <paramref name="open"/> (a profile's choices were just shown) the area opens
    /// while no concept model is chosen, so the first thing to do is in sight.
    /// </summary>
    private void ShowModels(bool open)
    {
        var model = Picker.Model(PipelineSteps.Concepts);
        ModelsHeader = model is null ? text["concepts.models.none"] : text.Format("concepts.models.header", model.Name);
        if (open && model is null)
        {
            ModelsOpen = true;
        }
    }

    private static StepPickerViewModel Layout(StepPickerViewModel picker)
    {
        picker.Compact = true;
        picker.Inline = InlineSteps;
        return picker;
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
        await AiEstimateAsync().ConfigureAwait(true);
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
            var size = await generator.BatchSizeAsync(profile, model, ConceptEffort(), CancellationToken.None).ConfigureAwait(true);
            var meaning = await generator.EstimateEmbeddingAsync(profile, embedding, count, CancellationToken.None, size).ConfigureAwait(true);
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
            // The step to choose is in the folded "Models": it is opened.
            TopicStatus = text["topics.no_model"];
            ModelsOpen = true;
            return;
        }

        AttachCalls();
        TopicFailed = false;
        TopicStatus = string.Empty;
        TopicRepeats = string.Empty;
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
            var result = await topics.SuggestAsync(profile, model, Focus(text.Language), ExtraPrompt, text.Language,
                new Progress<Core.Chat.ChatProgress>(TopicCall.Report), cancel.Token).ConfigureAwait(true);
            Topics.Clear();
            foreach (var topic in result.Topics)
            {
                Topics.Add(topic);
            }

            TopicRepeats = result.Repeats > 0 ? text.Format("topics.repeats", result.Repeats) : string.Empty;
            TopicFailed = result.Topics.Count == 0;
            TopicStatus = result.Topics.Count == 0
                ? text.Format(result.Repeats > 0 ? "topics.all_repeats" : "topics.none", text.Usd(result.CostUsd))
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
        catch (OperationCanceledException ex)
        {
            // What was counted for the canceled call is said (0 when it was canceled before anything was sent).
            logger.LogInformation("Topic suggestions canceled");
            TopicStatus = text.Format("topics.canceled", text.Usd(ex is TopicsCanceledException counted ? counted.EstimatedUsd : 0));
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

    /// <summary>
    /// Adds a suggested topic to the extra request (on a line of its own), takes it off the list and marks the kept suggestion used (a
    /// failure to mark it is only logged: the topic is in the extra request anyway).
    /// </summary>
    [RelayCommand]
    public async Task AddTopicAsync(string? topic)
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
        if (session.Profile is not { } profile)
        {
            return;
        }

        try
        {
            await suggestions.MarkUsedAsync(profile.Id, topic, CancellationToken.None).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "A clicked topic suggestion could not be marked used");
        }
    }

    /// <summary>The fine-tuning drop-downs, every one on "any" (another profile starts without a leftover choice).</summary>
    private void LoadFocus()
    {
        foreach (var row in FocusRows)
        {
            row.PropertyChanged -= OnFocusRowChanged;
        }

        FocusRows.Clear();
        foreach (var dimension in focus.Dimensions)
        {
            var options = new List<ChoiceOption> { new(string.Empty, text["focus.none"]) };
            options.AddRange(dimension.Options.Select(o => new ChoiceOption(o.Id, Name(o.Name))));
            var row = new FocusRowViewModel(dimension, Name(dimension.Name), options);
            row.PropertyChanged += OnFocusRowChanged;
            FocusRows.Add(row);
        }

        ShowFineTune();
        _ = SafeAsync(EstimateAsync);
    }

    private void OnFocusRowChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        ShowFineTune();
        _ = SafeAsync(EstimateAsync);
    }

    private void ShowFineTune()
    {
        var chosen = FocusRows.Where(r => r.Chosen is not null).Select(r => r.Selected!.Label).ToList();
        FineTuneHeader = chosen.Count == 0 ? text["focus.title"] : text["focus.title"] + " · " + string.Join(", ", chosen);
    }

    /// <summary>The chosen focus as the prompt's line in <paramref name="language"/> (empty when nothing is chosen).</summary>
    private string Focus(string language) =>
        FocusText.Build(language, FocusRows.Where(r => r.Chosen is not null).Select(r => (r.Dimension, r.Chosen!)));

    /// <summary>The effort chosen for the concept model now (empty = automatic).</summary>
    private string ConceptEffort() => Picker.Row(PipelineSteps.Concepts)?.Effort?.Value ?? string.Empty;

    private string Name(Core.Templates.LocalizedText name) => text.Language == Core.Profiles.ContentLanguages.Turkish ? name.Tr : name.En;

    private async Task TopicEstimateAsync(Core.Profiles.Profile profile)
    {
        if (Picker.Model(PipelineSteps.Topics) is not { } model)
        {
            TopicEstimate = text["topics.estimate.none"];
            TopicPrice = string.Empty;
            return;
        }

        var estimate = await topics.EstimateAsync(profile, model, Focus(text.Language), ExtraPrompt, text.Language, CancellationToken.None)
            .ConfigureAwait(true);
        TopicEstimate = text.Format("topics.estimate", model.Name, text.Usd(estimate.MinUsd), text.Usd(estimate.MaxUsd));
        TopicPrice = text.Format("topics.price", text.Usd(estimate.MinUsd), text.Usd(estimate.MaxUsd));
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
