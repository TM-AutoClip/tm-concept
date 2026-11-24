using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using TMConcept.Core.Activity;
using TMConcept.Core.Concepts;
using TMConcept.Core.Engines;
using TMConcept.Core.Images;
using TMConcept.Core.Models;
using TMConcept.Core.Notifications;
using TMConcept.Core.Pipeline;
using TMConcept.Core.Profiles;
using TMConcept.Core.Styles;
using TMConcept.Core.Videos;
using TMConcept.Desktop.Concepts;
using TMConcept.Desktop.Localization;
using TMConcept.Desktop.Startup;
using TMConcept.Desktop.Videos;

namespace TMConcept.Desktop.ViewModels;

/// <summary>
/// Concept → video (0.1.13 layout, the developer's request of 11 Nov 2026). On top one folded line with the page's models (video code with
/// its engine, images, narration, and the concept model for edits) and the template. Left: the approved concepts over the detail
/// panel (<see cref="ConceptDetailViewModel"/>): a click opens a concept there, where it can be edited by hand or with AI, taken back from
/// approved or deleted; nothing is added here, and only "Add to queue" on a row queues it. Middle: the queue (↑↓ order, ✕ back to approved);
/// the item being made shows every step with its time and cost, the code call's tokens and what was paid so far
/// (<see cref="ProductionProgressViewModel"/>); a failed item keeps its error with what it paid and can be tried again. Right: the made
/// videos with their thumbnails; a click loads the preview player, with "Open in player", "Open folder" and "Fix the concept, produce
/// again" (the concept comes back approved and opens in the detail panel). Below 1200 px the made videos go under the queue.
/// "Produce in order" first makes the choices ready (licenses, a key asked for when a provider has none, ONE confirmation that lists every
/// download) and saves them for the profile; declined or failed = nothing is made or paid for. The videos are made one by one: before each
/// item the queue is read again, so a removed item is not made, a new one is, and the order follows ↑↓; the models are the profile's
/// choices when the item starts, and so is the template (0.1.19, Settings → Templates; chosen here and kept on this computer,
/// <see cref="Paths.UiPreferences.VideoStyleId"/>, empty on a new install): nothing is produced until a code model and a template are chosen;
/// each video takes a copy of the template as it is stored when the video starts (<see cref="VideoStyleRules.Snapshot"/>), so a change of
/// the choice or of the template reaches the next video, never the one being made. Cancel stops after the running step.
/// <para>0.1.18 stop rules (<see cref="IVideoQueueRunner"/>): with "Night mode" off (the default at every start) any failed video stops the
/// whole queue (its error on the page, a notification and a red line); with it on (for when nobody is at the computer, a checkbox next to
/// "Produce in order", read at every decision so it can be ticked while the queue runs) a failed video is reported and the next one is made
/// after 10 s, a memory shortfall fails the video instead of asking, and three failures in a row stop the queue. Cancel in the memory
/// question stops the queue in both modes.</para>
/// </summary>
public sealed partial class VideoViewModel : PageViewModelBase
{
    private const int ProducedMax = 30;

    private readonly AppSession _session;
    private readonly IConceptStore _concepts;
    private readonly IQueueStore _queue;
    private readonly IProfileStore _profiles;
    private readonly VideoPipeline _pipeline;
    private readonly IReadOnlyList<IVideoEngine> _engines;
    private readonly StepModelResolver _resolver;
    private readonly Core.Media.IMediaTools _media;
    private readonly VideoCostEstimator _estimator;
    private readonly ConceptActions _actions;
    private readonly VideoRows _rows;
    private readonly VideoFiles _files;
    private readonly Func<ProductionProgressViewModel> _progress;
    private readonly ConceptEvents _events;
    private readonly INotifier _notifier;
    private readonly IVideoQueueRunner _runner;
    private readonly IActivityLog _activity;
    private readonly Localizer _text;
    private readonly ILogger<VideoViewModel> _logger;
    private readonly Paths.UiPreferencesFile _preferences;
    private readonly IVideoStyleStore _styles;
    private readonly Dictionary<Guid, Concept> _queued = [];
    private CancellationTokenSource? _cancel;
    private Guid? _running;
    private bool _attached;
    private bool _loading;
    private bool _choosing;
    private bool _loadingStyles;

    /// <summary>Creates the page with its models, its detail panel and the shared player.</summary>
    public VideoViewModel(AppSession session, IConceptStore concepts, IQueueStore queue, IProfileStore profiles, VideoPipeline pipeline,
        IEnumerable<IVideoEngine> engines, StepModelResolver resolver, Core.Media.IMediaTools media, VideoCostEstimator estimator, StepPickerViewModel picker,
        ConceptDetailViewModel detail, ConceptActions actions, VideoRows rows, VideoFiles files, PlayerViewModel player,
        Func<ProductionProgressViewModel> progress, ConceptEvents events, INotifier notifier, IVideoQueueRunner runner, IActivityLog activity, Localizer text,
        Paths.UiPreferencesFile preferences, IVideoStyleStore styles, ILogger<VideoViewModel> logger)
    {
        _session = session;
        _concepts = concepts;
        _queue = queue;
        _profiles = profiles;
        _pipeline = pipeline;
        _engines = engines.ToList();
        _resolver = resolver;
        _media = media;
        _estimator = estimator;
        _actions = actions;
        _rows = rows;
        _files = files;
        _progress = progress;
        _events = events;
        _notifier = notifier;
        _runner = runner;
        _activity = activity;
        _text = text;
        _logger = logger;

        _preferences = preferences;
        _styles = styles;
        Player = player;
        Detail = detail;

        // Four compact cards side by side when unfolded: code with its engine, images, narration, the concept model of the AI edit.
        Picker = picker;
        Picker.Columns = 4;
        Picker.Compact = true;
        Detail.Attach(Picker, this);
        Detail.Changed += (_, change) => _ = SafeAsync(() => AfterDetailAsync(change));
        Detail.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ConceptDetailViewModel.AiRunning))
            {
                Working = Busy || Detail.AiRunning;
            }
        };
    }

    /// <inheritdoc />
    public override string TitleKey => "nav.video";

    /// <inheritdoc />
    public override int Order => 20;

    /// <summary>The page's models (the profile's last choices).</summary>
    public StepPickerViewModel Picker { get; }

    /// <summary>Whether the model cards are unfolded (folded when the page opens).</summary>
    [ObservableProperty]
    public partial bool ModelsOpen { get; set; }

    /// <summary>Folds the model cards (their Close button, a click beside them).</summary>
    [RelayCommand]
    public void CloseModels() => ModelsOpen = false;

    /// <summary>The templates of Settings → Templates, by name (read again whenever the page is shown).</summary>
    public ObservableCollection<VideoStyle> Styles { get; } = [];

    /// <summary>
    /// The chosen template, kept on this computer (nothing on a new install). Read when each queued video starts, so a change applies to the
    /// next video, never to the one being made; nothing is produced while none is chosen.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StyleConceptNote), nameof(HasStyleConceptNote))]
    public partial VideoStyle? SelectedStyle { get; set; }

    /// <summary>
    /// 0.1.32: the chosen template's style changes how a concept should be written (the stick-figure narrator): a note that a concept written
    /// for no style or another one can be adapted on the concepts page (AI edit → adapt to a style). Empty otherwise.
    /// </summary>
    public string StyleConceptNote => SelectedStyle is { } style && Core.Styles.DrawStyles.Of(style.DrawStyle).HasConceptGuidance
        ? _text.Format("video.style.concept_note", _text.Has("style.draw." + style.DrawStyle.ToString().ToLowerInvariant())
            ? _text["style.draw." + style.DrawStyle.ToString().ToLowerInvariant()]
            : style.DrawStyle.ToString())
        : string.Empty;

    /// <summary>Whether <see cref="StyleConceptNote"/> is shown.</summary>
    public bool HasStyleConceptNote => StyleConceptNote.Length > 0;

    /// <summary>There is no template yet (the picker says to make one in Settings).</summary>
    [ObservableProperty]
    public partial bool NoStyles { get; set; }

    /// <summary>The detail panel under the approved list.</summary>
    public ConceptDetailViewModel Detail { get; }

    /// <summary>The preview player of the chosen made video (shared with the Produced page; one page is shown at a time).</summary>
    public PlayerViewModel Player { get; }

    /// <summary>Approved concepts.</summary>
    public ObservableCollection<ConceptCardViewModel> Approved { get; } = [];

    /// <summary>The approved row chosen (its concept is in the detail panel).</summary>
    [ObservableProperty]
    public partial ConceptCardViewModel? SelectedApproved { get; set; }

    /// <summary>"Approved (6)".</summary>
    [ObservableProperty]
    public partial string ApprovedHeader { get; set; } = string.Empty;

    /// <summary>The queue.</summary>
    public ObservableCollection<QueueRowViewModel> Queue { get; } = [];

    /// <summary>The queue row chosen (its concept is shown read-only in the detail panel).</summary>
    [ObservableProperty]
    public partial QueueRowViewModel? SelectedQueue { get; set; }

    /// <summary>"Production queue (3)".</summary>
    [ObservableProperty]
    public partial string QueueHeader { get; set; } = string.Empty;

    /// <summary>The estimate of the queued videos with the models chosen now, as one tiny line ("≈ 0.40 $–0.90 $").</summary>
    [ObservableProperty]
    public partial string QueueEstimate { get; set; } = string.Empty;

    /// <summary>The estimate in words (its tooltip).</summary>
    [ObservableProperty]
    public partial string QueueEstimateTip { get; set; } = string.Empty;

    /// <summary>The last video of this session: "Last: Theodosian walls · 0.38 $ · 6:12" (or what a failed one paid).</summary>
    [ObservableProperty]
    public partial string LastLine { get; set; } = string.Empty;

    /// <summary>The made videos, newest version of each, newest first.</summary>
    public ObservableCollection<VideoRowViewModel> Produced { get; } = [];

    /// <summary>The made video in the preview.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasProducedSelection))]
    public partial VideoRowViewModel? SelectedProduced { get; set; }

    /// <summary>Whether a made video is chosen (the preview and its buttons show).</summary>
    public bool HasProducedSelection => SelectedProduced is not null;

    /// <summary>"Produced (14)".</summary>
    [ObservableProperty]
    public partial string ProducedHeader { get; set; } = string.Empty;

    /// <summary>What the empty approved list says (empty when it has concepts).</summary>
    [ObservableProperty]
    public partial string ApprovedEmpty { get; set; } = string.Empty;

    /// <summary>What the empty queue says (empty when it has items).</summary>
    [ObservableProperty]
    public partial string QueueEmpty { get; set; } = string.Empty;

    /// <summary>"No videos yet." when there are none.</summary>
    [ObservableProperty]
    public partial string ProducedEmpty { get; set; } = string.Empty;

    /// <summary>The queue is being made.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Idle))]
    public partial bool Busy { get; set; }

    /// <summary>The queue is not being made.</summary>
    public bool Idle => !Busy;

    /// <summary>
    /// Night mode (not kept: off at every start): a failed video does not stop the queue, 10 s pass between videos, three failures in a row
    /// stop it. It can be changed while the queue runs; every decision reads it then.
    /// </summary>
    [ObservableProperty]
    public partial bool NightMode { get; set; }

    /// <inheritdoc />
    public override async Task ActivateAsync()
    {
        if (!_attached)
        {
            _attached = true;
            _events.Changed += async (sender, _) =>
            {
                if (!ReferenceEquals(sender, this) && !Busy)
                {
                    await SafeAsync(ReloadAsync).ConfigureAwait(true);
                }
            };
            Picker.Changed += async (_, _) =>
            {
                if (!_loading)
                {
                    // This page's pickers include the concept step: the concepts page keeps its model bar (0.1.40) and reads it again.
                    _events.RaiseSettings(this);
                    await SafeAsync(EstimateAsync).ConfigureAwait(true);
                }
            };
        }

        await LoadStylesAsync().ConfigureAwait(true);
        if (!Busy)
        {
            await LoadModelsAsync().ConfigureAwait(true);
        }

        await ReloadAsync().ConfigureAwait(true);
    }

    /// <inheritdoc />
    public override async Task ProfileChangedAsync()
    {
        if (!Busy)
        {
            LastLine = string.Empty;
            await Detail.LoadAsync(null).ConfigureAwait(true);
            await LoadModelsAsync().ConfigureAwait(true);
            await ReloadAsync().ConfigureAwait(true);
        }
    }

    /// <inheritdoc />
    public override Task<bool> CanLeaveAsync() => Detail.LeaveAsync();

    /// <inheritdoc />
    public override void Deactivate() => Player.Close();

    /// <inheritdoc />
    public override void CancelWork()
    {
        Cancel();
        Detail.CancelWork();
    }

    /// <summary>Opens <paramref name="card"/> in the detail panel, after asking when unsaved edits would be lost; false when the user stays.</summary>
    public async Task<bool> ChooseAsync(ConceptCardViewModel? card)
    {
        if (ReferenceEquals(card, Detail.Card))
        {
            return true;
        }

        if (!await Detail.LeaveAsync().ConfigureAwait(true))
        {
            Mark(Detail.Card);
            return false;
        }

        await Detail.LoadAsync(card).ConfigureAwait(true);
        Mark(card);
        return true;
    }

    /// <summary>Moves an approved concept into the queue (only this button queues; a click on the row opens the concept).</summary>
    [RelayCommand]
    public async Task EnqueueAsync(ConceptCardViewModel? card)
    {
        if (card is null)
        {
            return;
        }

        if (_session.Profile is not { } profile)
        {
            Refuse(_text["profile.needed"]);
            _session.AskForProfile();
            return;
        }

        if (ReferenceEquals(card, Detail.Card) && !await Detail.LeaveAsync().ConfigureAwait(true))
        {
            // Unsaved edits of this concept: the user stays with them, so it is not queued (a queued concept cannot be edited).
            return;
        }

        await _queue.AddAsync(new QueueItem
        {
            ProfileId = profile.Id,
            ConceptId = card.Id,
            Position = Queue.Count,
        }, CancellationToken.None).ConfigureAwait(true);
        await _concepts.SetStatusAsync([card.Id], ConceptStatus.Queued, CancellationToken.None).ConfigureAwait(true);
        _events.Raise(this);
        await ReloadAsync().ConfigureAwait(true);
    }

    /// <summary>Moves a queued concept back to the approved list (never the item that is being made).</summary>
    [RelayCommand]
    public async Task DequeueAsync(QueueRowViewModel? row)
    {
        if (row is null || row.Busy || row.Item.Id == _running)
        {
            return;
        }

        await _queue.RemoveAsync(row.Item.Id, CancellationToken.None).ConfigureAwait(true);
        await _concepts.SetStatusAsync([row.Item.ConceptId], ConceptStatus.Approved, CancellationToken.None).ConfigureAwait(true);
        _events.Raise(this);
        await ReloadAsync().ConfigureAwait(true);
    }

    /// <summary>Moves a queue row up.</summary>
    [RelayCommand]
    public Task UpAsync(QueueRowViewModel? row) => MoveAsync(row, -1);

    /// <summary>Moves a queue row down.</summary>
    [RelayCommand]
    public Task DownAsync(QueueRowViewModel? row) => MoveAsync(row, 1);

    /// <summary>Makes every queued video in order.</summary>
    [RelayCommand]
    public async Task ProduceAllAsync()
    {
        if (Busy)
        {
            return;
        }

        if (_session.Profile is not { } profile)
        {
            Refuse(_text["profile.needed"]);
            _session.AskForProfile();
            return;
        }

        Busy = true;
        using var cancel = new CancellationTokenSource();
        _cancel = cancel;
        var tried = new HashSet<Guid>();
        try
        {
            var items = await _queue.ListAsync(profile.Id, CancellationToken.None).ConfigureAwait(true);
            if (items.Count == 0)
            {
                // 0.1.15 ended at once without a word.
                Refuse(_text["video.queue.empty"]);
                return;
            }

            if (MissingChoice() is { } missing)
            {
                Refuse(missing);
                return;
            }

            if (!await PrepareAsync(items, cancel.Token).ConfigureAwait(true))
            {
                return;
            }

            // The queue is read again before every item: ✕, ↑↓ and new items during the run count; a failed item is tried once per press.
            var result = await _runner.RunAsync(async _ =>
            {
                var next = (await _queue.ListAsync(profile.Id, CancellationToken.None).ConfigureAwait(true)).FirstOrDefault(i => !tried.Contains(i.Id));
                if (next is null)
                {
                    return null;
                }

                tried.Add(next.Id);
                var row = Queue.FirstOrDefault(r => r.Item.Id == next.Id);
                if (row is null)
                {
                    await ReloadAsync().ConfigureAwait(true);
                    row = Queue.FirstOrDefault(r => r.Item.Id == next.Id) ?? new QueueRowViewModel(next, "?");
                }

                return row;
            }, ProduceAsync, () => NightMode, cancel.Token).ConfigureAwait(true);
            Ended(result);
        }
        catch (OperationCanceledException)
        {
            // Cancel before the first item started (while the choices were prepared): nothing was made, nothing to report.
            _logger.LogInformation("Producing the queue was cancelled before an item started");
        }
        finally
        {
            _cancel = null;
            Busy = false;
            await FreeCardAsync().ConfigureAwait(true);
            await ReloadAsync().ConfigureAwait(true);
        }
    }

    /// <summary>After the queue: the last local model leaves the graphics card (a server would otherwise keep it for its own idle time, LM Studio for good).</summary>
    private async Task FreeCardAsync()
    {
        try
        {
            await _pipeline.FreeCardAsync(CancellationToken.None).ConfigureAwait(true);

            // Work files older than their keep time go now too (until 0.1.28 only at the next start).
            await Task.Run(_pipeline.CleanOld).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _logger.LogWarning(ex, "The graphics card could not be freed or the old work files cleaned after the queue");
        }
    }

    /// <summary>Tries one failed row again.</summary>
    [RelayCommand]
    public async Task RetryAsync(QueueRowViewModel? row)
    {
        if (row is null || Busy)
        {
            return;
        }

        if (MissingChoice() is { } missing)
        {
            Refuse(missing);
            return;
        }

        Busy = true;
        using var cancel = new CancellationTokenSource();
        _cancel = cancel;
        try
        {
            if (!await PrepareAsync([row.Item], cancel.Token).ConfigureAwait(true))
            {
                return;
            }

            if (await ProduceAsync(row, cancel.Token).ConfigureAwait(true) == QueueItemOutcome.Stopped)
            {
                Status = _text["video.queue.stopped.memory"];
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("The retry was cancelled before the item started");
        }
        finally
        {
            _cancel = null;
            Busy = false;
            await ReloadAsync().ConfigureAwait(true);
        }
    }

    /// <summary>Stops after the running step.</summary>
    [RelayCommand]
    public void Cancel() => _cancel?.Cancel();

    /// <summary>A press that cannot run: the reason on the page's (red) status line and in the activity panel.</summary>
    private void Refuse(string reason)
    {
        Status = reason;
        _activity.Add(ActivityLevel.Warning, reason);
    }

    /// <summary>Opens the chosen made video in the system's player.</summary>
    [RelayCommand]
    public void PlaySystem()
    {
        if (SelectedProduced is { } row)
        {
            Status = _files.Play(row);
        }
    }

    /// <summary>Opens the chosen made video's folder.</summary>
    [RelayCommand]
    public void OpenFolder()
    {
        if (SelectedProduced is { } row)
        {
            Status = _files.OpenFolder(row);
        }
    }

    /// <summary>
    /// "Fix the concept, produce again": the chosen video's concept goes back to the approved ones and opens in the detail panel, one click
    /// from Edit or Edit with AI; "Add to queue" then makes a new video of it.
    /// </summary>
    [RelayCommand]
    public async Task ProduceAgainAsync()
    {
        if (SelectedProduced is not { } row)
        {
            return;
        }

        var (ok, error) = await _actions.ProduceAgainAsync(row.Video.ConceptId, this).ConfigureAwait(true);
        if (!ok)
        {
            Status = _text[error];
            return;
        }

        Status = string.Empty;
        await ReloadAsync().ConfigureAwait(true);
        await ChooseAsync(Approved.FirstOrDefault(c => c.Id == row.Video.ConceptId)).ConfigureAwait(true);
    }

    partial void OnBusyChanged(bool value) => Working = value || Detail.AiRunning;

    partial void OnSelectedStyleChanged(VideoStyle? value)
    {
        // Filled from the kept choice when the list is read: nothing to keep or estimate.
        if (_loadingStyles)
        {
            return;
        }

        KeepStyle(value?.Id);
        _ = SafeAsync(EstimateAsync);
    }

    /// <summary>Reads the templates again and chooses the kept one (no longer there, e.g. deleted in Settings: nothing is chosen).</summary>
    private async Task LoadStylesAsync()
    {
        var all = await _styles.ListAsync(CancellationToken.None).ConfigureAwait(true);
        var kept = _preferences.Read().VideoStyleId;
        _loadingStyles = true;
        try
        {
            Styles.Clear();
            foreach (var style in all)
            {
                Styles.Add(style);
            }

            SelectedStyle = Styles.FirstOrDefault(s => s.Id == kept);
        }
        finally
        {
            _loadingStyles = false;
        }

        NoStyles = Styles.Count == 0;
        if (kept is not null && SelectedStyle is null)
        {
            _logger.LogInformation("The kept template {Id} is gone; none is chosen", kept);
            KeepStyle(null);
        }
    }

    private void KeepStyle(Guid? id)
    {
        if (_preferences.Read().VideoStyleId == id)
        {
            return;
        }

        try
        {
            _preferences.Change(p => p.VideoStyleId = id);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The choice still holds for this run of the app; only the next start would not know it (logged, like the other kept choices).
            _logger.LogWarning(ex, "The template choice could not be kept");
        }
    }

    /// <summary>What must still be chosen before anything is produced: a code model and a template; <c>null</c> when both are.</summary>
    private string? MissingChoice()
    {
        if (SelectedStyle is not null)
        {
            return null;
        }

        return Picker.Model(PipelineSteps.VideoCode) is null ? _text["video.style.both"] : _text["video.style.none"];
    }

    partial void OnSelectedApprovedChanged(ConceptCardViewModel? value)
    {
        // A row leaving the list sets the selection to nothing: the panel keeps its concept.
        if (value is not null && !_choosing)
        {
            _ = SafeAsync(() => ChooseAsync(value));
        }
    }

    partial void OnSelectedQueueChanged(QueueRowViewModel? value)
    {
        if (value is not null && !_choosing && _queued.TryGetValue(value.Item.ConceptId, out var concept))
        {
            _ = SafeAsync(() => ChooseAsync(new ConceptCardViewModel(concept, _text)));
        }
    }

    partial void OnSelectedProducedChanged(VideoRowViewModel? value)
    {
        if (value is null)
        {
            Player.Unload();
            return;
        }

        // The poster shows; ▶ plays (nothing starts by itself).
        Player.Load(value.File, value.Thumbnail, value.Video.Width, value.Video.Height, value.Video.Fps, value.Video.DurationMs);
    }

    /// <summary>The panel took its concept back or deleted it: it left the approved list, and the panel closes (nothing is added here).</summary>
    private async Task AfterDetailAsync(ConceptStatusChange change)
    {
        if (change.Status != ConceptStatus.Approved)
        {
            await Detail.LoadAsync(null).ConfigureAwait(true);
        }

        await ReloadAsync().ConfigureAwait(true);
    }

    /// <summary>The list rows match the panel's concept (or nothing), without opening anything again.</summary>
    private void Mark(ConceptCardViewModel? card)
    {
        _choosing = true;
        try
        {
            SelectedApproved = card is not null && Approved.Contains(card) ? card : null;
            SelectedQueue = card is null ? null : Queue.FirstOrDefault(r => r.Item.ConceptId == card.Id);
        }
        finally
        {
            _choosing = false;
        }
    }

    private async Task LoadModelsAsync()
    {
        _loading = true;
        try
        {
            var profile = _session.Profile;
            var language = profile?.ContentLanguage ?? (_text.Language == "tr" ? ContentLanguages.Turkish : ContentLanguages.English);
            await Picker.LoadAsync(profile?.Id, language, PipelineSteps.VideoPage).ConfigureAwait(true);
        }
        finally
        {
            _loading = false;
        }
    }

    /// <summary>
    /// Makes the page's choices ready for the given queue items and saves them for the profile: the code model is required; the image model
    /// and the voice only when chosen; ffmpeg and the engine always (one confirmation for every download). The concept model is not made
    /// ready here: a video does not use it. Declined, failed or cancelled: the items get the reason and nothing is made.
    /// </summary>
    private async Task<bool> PrepareAsync(IReadOnlyList<QueueItem> items, CancellationToken cancellationToken)
    {
        if (items.Count == 0)
        {
            return true;
        }

        var needed = new List<string> { PipelineSteps.VideoCode };
        needed.AddRange(new[] { PipelineSteps.Images, PipelineSteps.ImagePoints, PipelineSteps.Speech }.Where(s => Picker.Model(s) is not null));
        bool ready;
        try
        {
            ready = await Picker.PrepareAsync(needed, "install.purpose.video", cancellationToken, _media.RequiredComponents).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Videos not started: cancelled while preparing");
            ready = false;
        }

        if (ready)
        {
            Status = string.Empty;
            return true;
        }

        var reason = Picker.Status.Length > 0 ? Picker.Status : _text["install.cancelled"];
        _logger.LogWarning("Videos not started: the chosen models are not ready");
        foreach (var row in Queue.Where(r => items.Any(i => i.Id == r.Item.Id)))
        {
            row.Error = _text["video.error.missing"] + " " + reason;
        }

        Status = reason;
        _activity.Add(ActivityLevel.Warning, _text["video.error.missing"] + " " + reason);
        return false;
    }

    /// <summary>
    /// How a run of the queue ended, on the page's (red) status line and in the activity panel. A failure with night mode off: the video's own
    /// red line and notification said why, so the stop is a yellow line under it (its notification stays the last one). Three failures in a
    /// row in night mode: a red line and a notification of their own. The memory question's Cancel: a yellow line. A queue that ran out of
    /// items or was canceled says nothing more.
    /// </summary>
    private void Ended(QueueRunResult result)
    {
        switch (result.End)
        {
            case QueueRunEnd.Failed:
                Status = _text["video.queue.stopped.error"];
                _activity.Add(ActivityLevel.Warning, Status);
                break;
            case QueueRunEnd.FailuresInRow:
                Status = _text.Format("video.queue.stopped.night", VideoQueueRunner.FailuresInRow);
                _activity.Add(ActivityLevel.Error, Status);
                _notifier.Notify(new AppNotification(NotificationKind.Failed, _text["video.queue.stopped.title"], Status));
                break;
            case QueueRunEnd.Stopped:
                Status = _text["video.queue.stopped.memory"];
                _activity.Add(ActivityLevel.Warning, Status);
                break;
        }
    }

    /// <summary>
    /// A video that fails before it starts (its profile is gone, its code model has no key or is not installed): the same red line (logged as
    /// an error) and notification as a failure inside the pipeline, so the stop that follows with night mode off is never silent.
    /// </summary>
    private void FailedBeforeStart(QueueRowViewModel row, string error)
    {
        row.Error = error;
        var line = _text.Format("video.failed", row.Title, error, _text.Usd(0));
        _activity.Add(ActivityLevel.Error, line, _text.Format("video.failed", ActivityPlaceholders.Title, error, _text.Usd(0)));
        _notifier.Notify(new AppNotification(NotificationKind.Failed, _text["video.failed.title"], line));
    }

    private async Task<QueueItemOutcome> ProduceAsync(QueueRowViewModel row, CancellationToken cancellationToken)
    {
        var outcome = QueueItemOutcome.Failed;
        row.Error = string.Empty;
        row.Busy = true;
        _running = row.Item.Id;
        try
        {
            // The item may have been removed (✕) after the run started: then it is not made and costs nothing.
            var stored = (await _queue.ListAsync(row.Item.ProfileId, CancellationToken.None).ConfigureAwait(true)).FirstOrDefault(i => i.Id == row.Item.Id);
            var concept = stored is null ? null : await _concepts.GetAsync(row.Item.ConceptId, CancellationToken.None).ConfigureAwait(true);
            if (stored is null || concept is not { Status: ConceptStatus.Queued })
            {
                _logger.LogInformation("Queue item skipped: it was removed or its concept is no longer queued");
                return outcome = QueueItemOutcome.Skipped;
            }

            if (await _profiles.GetAsync(row.Item.ProfileId, CancellationToken.None).ConfigureAwait(true) is not { } profile)
            {
                FailedBeforeStart(row, _text["video.error.concept"]);
                return outcome;
            }

            // Everything is the choice of now: the template (an item queued before it was changed follows it too; a copy as it is stored now,
            // so an edit in Settings reaches this video) and the profile's models (saved when the run was prepared): code model and engine,
            // images, narration.
            if (SelectedStyle is not { } chosen)
            {
                FailedBeforeStart(row, _text["video.style.none"]);
                return outcome;
            }

            if (await _styles.GetAsync(chosen.Id, CancellationToken.None).ConfigureAwait(true) is not { } style)
            {
                _logger.LogWarning("Video not started: the chosen template {Id} was deleted", chosen.Id);
                FailedBeforeStart(row, _text["video.style.gone"]);
                return outcome;
            }

            var options = new VideoOptions();
            VideoStyleRules.Snapshot(options, style);
            _logger.LogInformation("Queued video starts with template {Id} (subtitles {Subtitles}, progress bar {ProgressBar}, closing {Closing})",
                style.Id, style.Subtitles ? "on" : "off", style.ProgressBar ? "on" : "off", style.Closing);
            ResolvedModel model;
            var choice = await _resolver.ChoiceAsync(PipelineSteps.VideoCode, profile.Id, CancellationToken.None).ConfigureAwait(true);
            try
            {
                model = await _resolver.ResolveAsync(PipelineSteps.VideoCode, choice, CancellationToken.None).ConfigureAwait(true);
            }
            catch (ModelNotReadyException ex)
            {
                _logger.LogWarning("Video not started: the {Step} step has no {Missing}", ex.Step, ex.Missing);
                FailedBeforeStart(row, ex.Missing == "key" ? _text.Format("concepts.no_key", choice?.Provider ?? string.Empty) : _text["video.no_model"]);
                return outcome;
            }

            options.Engine = StepModelResolver.Options(choice).Engine;
            options.CodeProvider = model.Model.Provider;
            options.CodeModel = model.Model.Id;
            options.Images = ImageSourceOf(await _resolver.ChoiceAsync(PipelineSteps.Images, profile.Id, CancellationToken.None).ConfigureAwait(true));
            options.Speech = !StepModelResolver.IsOff(await _resolver.ChoiceAsync(PipelineSteps.Speech, profile.Id, CancellationToken.None).ConfigureAwait(true));

            var progress = _progress();
            row.Progress = progress;
            progress.Begin("work.video", model.Model.Name);
            var made = false;
            try
            {
                // Night mode is read when a memory shortfall happens (ticked meanwhile: nobody is asked, the video fails).
                VideoRecord record;
                using (MemoryShortfallScope.Begin(() => NightMode))
                {
                    record = await _pipeline.RunAsync(new VideoJob(concept, profile, options, model), progress.Reporter, cancellationToken).ConfigureAwait(true);
                }

                made = true;
                outcome = QueueItemOutcome.Made;
                await _queue.RemoveAsync(row.Item.Id, CancellationToken.None).ConfigureAwait(true);
                Report(record);
            }
            finally
            {
                await progress.FinishAsync(made).ConfigureAwait(true);
            }
        }
        catch (VideoException ex)
        {
            // The pipeline wrote the cause (with its step and code); the activity line below goes to the log too. The failure and what it
            // paid stay with the queue item, so the row still says it after a restart.
            row.Item.LastErrorCode = ex.Code;
            row.Item.LastErrorDetail = ex.Message;
            row.Item.FailedUsd += ex.SpentUsd;
            await KeepFailureAsync(row.Item, ex).ConfigureAwait(true);
            row.Error = FailureText(row.Item);
            var reason = Reason(ex.Code, ex.Message, ex.SpentUsd);
            LastLine = _text.Format("video.last.failed", row.Title, _text.Usd(ex.SpentUsd));
            _activity.Add(ActivityLevel.Error, _text.Format("video.failed", row.Title, reason, _text.Usd(ex.SpentUsd)),
                _text.Format("video.failed", ActivityPlaceholders.Title, reason, _text.Usd(ex.SpentUsd)));
            // The warnings gathered before the failure are still said (narration off…), after the failure itself.
            var noted = Noted(row.Title, ex.Warnings);

            // The notification carries the reason and what was paid (the strip at the bottom shows the activity line too).
            _notifier.Notify(new AppNotification(NotificationKind.Failed, _text["video.failed.title"],
                string.Join('\n', noted.Prepend(_text.Format("video.failed", row.Title, reason, _text.Usd(ex.SpentUsd))))));
        }
        catch (MemoryShortfallCanceledException)
        {
            // Cancel in the memory question: the whole queue stops (the app stays open); the activity panel has the step already.
            row.Error = _text["video.queue.stopped.memory"];
            outcome = QueueItemOutcome.Stopped;
        }
        catch (OperationCanceledException)
        {
            row.Error = _text["video.canceled"];
            _activity.Add(ActivityLevel.Warning, _text.Format("video.canceled.log", row.Title), _text.Format("video.canceled.log", ActivityPlaceholders.Title));
            outcome = QueueItemOutcome.Canceled;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Any other error (a file gone, the graphics card gone, a crash of a step) fails the video the same way: with night mode off the
            // queue stops on it, with night mode on the next video is made.
            _logger.LogError(ex, "Video failed");
            row.Error = ex.Message;
            _activity.Add(ActivityLevel.Error, _text.Format("video.failed", row.Title, ex.Message, _text.Usd(0)),
                _text.Format("video.failed", ActivityPlaceholders.Title, ex.Message, _text.Usd(0)));
            _notifier.Notify(new AppNotification(NotificationKind.Failed, _text["video.failed.title"], _text.Format("video.failed", row.Title, ex.Message, _text.Usd(0))));
        }
        finally
        {
            row.Busy = false;
            _running = null;

            // The job's buffers (scene images, audio, model answers) are garbage now: give the memory back (small machines).
            Memory.MemoryRelease.Now(_logger, "a video job");
        }

        // The page's lists follow each video at once: a made one leaves the queue and stands under Produced, a failed one keeps its row
        // (0.1.23: they changed only after leaving the page and coming back, because the page skips its own change event while it runs).
        await SafeAsync(ReloadAsync).ConfigureAwait(true);
        return outcome;
    }

    /// <summary>Where the scene images come from for a saved images choice: a model, or none.</summary>
    private static string ImageSourceOf(StepModelChoice? choice) => StepModelResolver.IsOff(choice) ? ImageSources.None : ImageSources.Model;

    /// <summary>
    /// A made video: the last line of the page, one notification with the cost and the problems of the video (silent, no images…; they are
    /// also badges and notes on the Produced videos page), every line in the log file.
    /// </summary>
    private void Report(VideoRecord record)
    {
        var warnings = record.Warnings.Split(',', StringSplitOptions.RemoveEmptyEntries);
        var problems = new List<string>();
        foreach (var (code, key, level, _) in Problems)
        {
            if (warnings.Contains(code))
            {
                _activity.Add(level, _text.Format(key, record.Title), _text.Format(key, ActivityPlaceholders.Title));
                problems.Add(_text.Format(key, record.Title));
            }
        }

        var seconds = (record.ElapsedMs / 1000.0).ToString("0", CultureInfo.InvariantCulture);
        LastLine = _text.Format("video.last", record.Title, _text.Usd(record.TotalUsd),
            TimeSpan.FromMilliseconds(record.ElapsedMs).ToString(@"m\:ss", CultureInfo.InvariantCulture));
        var done = _text.Format("video.done", record.Title, _text.Usd(record.TotalUsd), seconds);
        _activity.Add(ActivityLevel.Info, done, _text.Format("video.done", ActivityPlaceholders.Title, _text.Usd(record.TotalUsd), seconds));
        _notifier.Notify(new AppNotification(problems.Count == 0 ? NotificationKind.Done : NotificationKind.Warning, _text["video.done.title"],
            string.Join("\n", problems.Prepend(done))));
        _events.Raise(this);
    }

    /// <summary>
    /// The warnings of a video that the notification names (warning code, text key of a made video, log level, text key when the run failed
    /// later: "was made silent" would be wrong then).
    /// </summary>
    private static readonly (string Code, string Key, ActivityLevel Level, string NotedKey)[] Problems =
    [
        (VideoWarnings.SpeechOff, "video.silent.off", ActivityLevel.Warning, "video.noted.speech_off"),
        (VideoWarnings.SpeechSkipped, "video.speech.skipped", ActivityLevel.Warning, "video.noted.speech_skipped"),
        (VideoWarnings.FrameCheck, "video.frames.fallback", ActivityLevel.Warning, "video.noted.frame_check"),
        (VideoWarnings.FrameWeak, "video.frames.weak", ActivityLevel.Warning, "video.noted.frame_weak"),
        (VideoWarnings.NoThumbnail, "video.thumbnail.none", ActivityLevel.Warning, "video.noted.no_thumbnail"),

        // The motion check of the finished video (0.1.41): yellow, the video is kept; the times are in the log, the strips in checks/.
        .. VideoWarnings.Motion.Select(code => (code, "video." + code, ActivityLevel.Warning, "video.noted." + code)),
    ];

    /// <summary>The warnings of a failed run as activity lines (Warning); returns their texts for the notification.</summary>
    private List<string> Noted(string title, IReadOnlyList<string> warnings)
    {
        var texts = new List<string>();
        foreach (var key in Problems.Where(p => warnings.Contains(p.Code)).Select(p => p.NotedKey))
        {
            texts.Add(_text.Format("video.failed.noted", title, _text[key]));
            _activity.Add(ActivityLevel.Warning, texts[^1], _text.Format("video.failed.noted", ActivityPlaceholders.Title, _text[key]));
        }

        return texts;
    }

    /// <summary>Why a video failed (<see cref="VideoFailures"/>).</summary>
    private string Reason(string code, string detail, decimal spentUsd) => VideoFailures.Reason(_text, code, detail, spentUsd);

    /// <summary>The row's error: the last failure and what the failed tries of this item paid together ("spent because of the failure").</summary>
    private string FailureText(QueueItem item)
    {
        var reason = Reason(item.LastErrorCode, item.LastErrorDetail, item.FailedUsd);
        return item.FailedUsd <= 0 || VideoFailures.IsTruncated(item.LastErrorCode)
            ? reason
            : reason + " " + _text.Format("video.failed.spent", _text.Usd(item.FailedUsd));
    }

    private async Task KeepFailureAsync(QueueItem item, VideoException failure)
    {
        try
        {
            await _queue.RecordFailureAsync(item.Id, failure.Code, failure.Message, failure.SpentUsd, CancellationToken.None).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The row shows it anyway; only after a restart it would be gone.
            _logger.LogError(ex, "The failure of a queue item could not be kept");
        }
    }

    private async Task MoveAsync(QueueRowViewModel? row, int step)
    {
        if (row is null || _session.Profile is not { } profile)
        {
            return;
        }

        var index = Queue.IndexOf(row);
        var target = index + step;
        if (index < 0 || target < 0 || target >= Queue.Count)
        {
            return;
        }

        Queue.Move(index, target);
        await _queue.ReorderAsync(profile.Id, Queue.Select(r => r.Item.Id).ToList(), CancellationToken.None).ConfigureAwait(true);
    }

    /// <summary>The estimate of the queued videos with the models chosen now (empty for an empty queue).</summary>
    private async Task EstimateAsync()
    {
        QueueEstimate = string.Empty;
        QueueEstimateTip = string.Empty;
        if (_session.Profile is not { } profile || Queue.Count == 0)
        {
            return;
        }

        if (Picker.Model(PipelineSteps.VideoCode) is not { } code)
        {
            QueueEstimate = _text["video.estimate.none"];
            return;
        }

        var queued = new List<Concept>();
        foreach (var row in Queue)
        {
            if (await _concepts.GetAsync(row.Item.ConceptId, CancellationToken.None).ConfigureAwait(true) is { } concept)
            {
                queued.Add(concept);
            }
        }

        var engine = _engines.FirstOrDefault(e => e.Kind == Picker.Row(PipelineSteps.VideoCode)?.Engine?.Kind);
        var estimate = _estimator.Estimate(queued, profile, engine, code, Picker.Model(PipelineSteps.Images), Picker.Model(PipelineSteps.Speech),
            SelectedStyle?.Subtitles ?? true, SelectedStyle);
        QueueEstimate = _text.Format("concepts.estimate.tiny", _text.Usd(estimate.MinUsd), _text.Usd(estimate.MaxUsd));
        QueueEstimateTip = _text.Format("video.estimate", queued.Count, _text.Usd(estimate.MinUsd), _text.Usd(estimate.MaxUsd));
    }

    /// <summary>
    /// Reads the lists, then rebuilds them in one go (no await between clearing and filling, so two reloads cannot mix). A queue row that is
    /// still in the queue keeps its object: the running item keeps its stages and stays locked, a failed one keeps its error. Cards and
    /// video rows still listed keep theirs, so the open concept and the chosen video stay.
    /// </summary>
    private async Task ReloadAsync()
    {
        if (_session.Profile is not { } profile)
        {
            Approved.Clear();
            Queue.Clear();
            Produced.Clear();
            _queued.Clear();
            Headers();
            await EstimateAsync().ConfigureAwait(true);
            return;
        }

        var approved = await _concepts.ListAsync(profile.Id, [ConceptStatus.Approved], false, CancellationToken.None).ConfigureAwait(true);
        var queued = (await _concepts.ListAsync(profile.Id, [ConceptStatus.Queued], false, CancellationToken.None).ConfigureAwait(true))
            .DistinctBy(c => c.Id).ToDictionary(c => c.Id);
        var items = await _queue.ListAsync(profile.Id, CancellationToken.None).ConfigureAwait(true);
        var produced = await _rows.LatestAsync(profile.Id, Produced, ProducedMax, CancellationToken.None).ConfigureAwait(true);
        if (_session.Profile?.Id != profile.Id)
        {
            return;
        }

        var cards = Approved.ToDictionary(c => c.Id);
        if (Detail.Card is { } open)
        {
            cards.TryAdd(open.Id, open);
        }

        CollectionSync.Apply(Approved, approved.DistinctBy(c => c.Id).Select(c =>
        {
            if (!cards.TryGetValue(c.Id, out var card))
            {
                return new ConceptCardViewModel(c, _text);
            }

            card.Concept.Status = c.Status;
            return card;
        }).ToList());
        _queued.Clear();
        foreach (var (id, concept) in queued)
        {
            _queued[id] = concept;
        }

        var rows = Queue.ToDictionary(r => r.Item.Id);
        CollectionSync.Apply(Queue, items.Select(item => rows.TryGetValue(item.Id, out var row) ? row : new QueueRowViewModel(item,
            queued.TryGetValue(item.ConceptId, out var concept) ? "#" + concept.Number.ToString(CultureInfo.InvariantCulture) + " " + concept.Title : "?")
        {
            Error = item.LastErrorCode.Length > 0 ? FailureText(item) : string.Empty,
        }).ToList());
        var chosen = SelectedProduced?.Video.Id;
        CollectionSync.Apply(Produced, produced.ToList());
        if (chosen is not null && SelectedProduced is null)
        {
            SelectedProduced = Produced.FirstOrDefault(r => r.Video.Id == chosen);
        }

        Headers();
        Mark(Detail.Card);
        if (Detail.Card is { } shown && !Approved.Contains(shown) && !Detail.Unsaved)
        {
            // Queued now: the panel shows it read-only. Produced, taken back or deleted elsewhere: it left the page and the panel closes.
            if (!_queued.ContainsKey(shown.Id))
            {
                await Detail.LoadAsync(null).ConfigureAwait(true);
            }
            else if (shown.Concept.Status != ConceptStatus.Queued)
            {
                await Detail.LoadAsync(shown).ConfigureAwait(true);
            }
        }

        Badge = Queue.Count == 0 ? string.Empty : Queue.Count.ToString(CultureInfo.InvariantCulture);
        await EstimateAsync().ConfigureAwait(true);
    }

    private void Headers()
    {
        ApprovedHeader = _text.Format("video.approved", Approved.Count);
        QueueHeader = _text.Format("video.queue", Queue.Count);
        ProducedHeader = _text.Format("video.produced", Produced.Count);
        ProducedEmpty = Produced.Count == 0 ? _text["video.produced.empty"] : string.Empty;
        ApprovedEmpty = Approved.Count == 0 ? _text["video.approved.empty"] : string.Empty;
        QueueEmpty = Queue.Count == 0 ? _text["video.queue.empty"] : string.Empty;
    }

    private async Task SafeAsync(Func<Task> work)
    {
        try
        {
            await work().ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "The video page could not be updated");
            Status = _text.Format("error.generic", ex.Message);
        }
    }
}
