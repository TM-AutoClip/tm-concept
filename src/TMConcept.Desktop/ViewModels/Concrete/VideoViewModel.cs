using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
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
using TMConcept.Core.Videos;
using TMConcept.Desktop.Concepts;
using TMConcept.Desktop.Localization;
using TMConcept.Desktop.Startup;
using TMConcept.Desktop.Videos;

namespace TMConcept.Desktop.ViewModels;

/// <summary>
/// Concept → video (0.1.13 layout, the developer's request of 11 Nov 2026). On top one folded line with the page's models (video code with
/// its engine, images, narration, and the concept model for edits) and the subtitles choice. Left: the approved concepts over the detail
/// panel (<see cref="ConceptDetailViewModel"/>): a click opens a concept there, where it can be edited by hand or with AI, taken back from
/// approved or deleted; nothing is added here, and only "Add to queue" on a row queues it. Middle: the queue (↑↓ order, ✕ back to approved);
/// the item being made shows every step with its time and cost, the code call's tokens and what was paid so far
/// (<see cref="ProductionProgressViewModel"/>); a failed item keeps its error with what it paid and can be tried again. Right: the made
/// videos with their thumbnails; a click loads the preview player, with "Open in player", "Open folder" and "Fix the concept, produce
/// again" (the concept comes back approved and opens in the detail panel). Below 1200 px the made videos go under the queue.
/// "Produce in order" first makes the choices ready (licenses, a key asked for when a provider has none, ONE confirmation that lists every
/// download) and saves them for the profile; declined or failed = nothing is made or paid for. The videos are made one by one: before each
/// item the queue is read again, so a removed item is not made, a new one is, and the order follows ↑↓; the models are the profile's
/// choices when the item starts (a queue item keeps only its subtitles choice). Cancel stops after the running step.
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
    private readonly IActivityLog _activity;
    private readonly Localizer _text;
    private readonly ILogger<VideoViewModel> _logger;
    private readonly Dictionary<Guid, Concept> _queued = [];
    private CancellationTokenSource? _cancel;
    private Guid? _running;
    private bool _attached;
    private bool _loading;
    private bool _choosing;

    /// <summary>Creates the page with its models, its detail panel and the shared player.</summary>
    public VideoViewModel(AppSession session, IConceptStore concepts, IQueueStore queue, IProfileStore profiles, VideoPipeline pipeline,
        IEnumerable<IVideoEngine> engines, StepModelResolver resolver, Core.Media.IMediaTools media, VideoCostEstimator estimator, StepPickerViewModel picker,
        ConceptDetailViewModel detail, ConceptActions actions, VideoRows rows, VideoFiles files, PlayerViewModel player,
        Func<ProductionProgressViewModel> progress, ConceptEvents events, INotifier notifier, IActivityLog activity, Localizer text,
        ILogger<VideoViewModel> logger)
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
        _activity = activity;
        _text = text;
        _logger = logger;
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

    /// <summary>Subtitles in the picture (kept with each queue item).</summary>
    [ObservableProperty]
    public partial bool Subtitles { get; set; } = true;

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
                    await SafeAsync(EstimateAsync).ConfigureAwait(true);
                }
            };
        }

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
        if (card is null || _session.Profile is not { } profile)
        {
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
            SettingsJson = JsonSerializer.Serialize(Options(), Core.Render.CoreJsonContext.Default.VideoOptions),
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
            Status = _text["picker.no_profile"];
            return;
        }

        Busy = true;
        using var cancel = new CancellationTokenSource();
        _cancel = cancel;
        var tried = new HashSet<Guid>();
        try
        {
            var items = await _queue.ListAsync(profile.Id, CancellationToken.None).ConfigureAwait(true);
            if (!await PrepareAsync(items, cancel.Token).ConfigureAwait(true))
            {
                return;
            }

            // The queue is read again before every item: ✕, ↑↓ and new items during the run count; a failed item is tried once per press.
            while (!cancel.IsCancellationRequested)
            {
                var next = (await _queue.ListAsync(profile.Id, CancellationToken.None).ConfigureAwait(true)).FirstOrDefault(i => !tried.Contains(i.Id));
                if (next is null)
                {
                    break;
                }

                tried.Add(next.Id);
                var row = Queue.FirstOrDefault(r => r.Item.Id == next.Id);
                if (row is null)
                {
                    await ReloadAsync().ConfigureAwait(true);
                    row = Queue.FirstOrDefault(r => r.Item.Id == next.Id) ?? new QueueRowViewModel(next, "?");
                }

                await ProduceAsync(row, cancel.Token).ConfigureAwait(true);
            }
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
            await ReloadAsync().ConfigureAwait(true);
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

        Busy = true;
        using var cancel = new CancellationTokenSource();
        _cancel = cancel;
        try
        {
            if (!await PrepareAsync([row.Item], cancel.Token).ConfigureAwait(true))
            {
                return;
            }

            await ProduceAsync(row, cancel.Token).ConfigureAwait(true);
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

    partial void OnSubtitlesChanged(bool value) => _ = SafeAsync(EstimateAsync);

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
        needed.AddRange(new[] { PipelineSteps.Images, PipelineSteps.Speech }.Where(s => Picker.Model(s) is not null));
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

    private async Task ProduceAsync(QueueRowViewModel row, CancellationToken cancellationToken)
    {
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
                return;
            }

            if (await _profiles.GetAsync(row.Item.ProfileId, CancellationToken.None).ConfigureAwait(true) is not { } profile)
            {
                row.Error = _text["video.error.concept"];
                return;
            }

            // The item keeps only its subtitles; the models are the profile's choices now (saved when the run was prepared): code model
            // and engine, images, narration.
            var options = new VideoOptions { Subtitles = Stored(stored).Subtitles };
            ResolvedModel model;
            var choice = await _resolver.ChoiceAsync(PipelineSteps.VideoCode, profile.Id, CancellationToken.None).ConfigureAwait(true);
            try
            {
                model = await _resolver.ResolveAsync(PipelineSteps.VideoCode, choice, CancellationToken.None).ConfigureAwait(true);
            }
            catch (ModelNotReadyException ex)
            {
                _logger.LogWarning("Video not started: the {Step} step has no {Missing}", ex.Step, ex.Missing);
                row.Error = ex.Missing == "key" ? _text.Format("concepts.no_key", choice?.Provider ?? string.Empty) : _text["video.no_model"];
                return;
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
                var record = await _pipeline.RunAsync(new VideoJob(concept, profile, options, model), progress.Reporter, cancellationToken)
                    .ConfigureAwait(true);
                made = true;
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
            // The notification carries the reason and what was paid (the strip at the bottom shows the activity line too).
            _notifier.Notify(new AppNotification(NotificationKind.Failed, _text["video.failed.title"],
                _text.Format("video.failed", row.Title, reason, _text.Usd(ex.SpentUsd))));
        }
        catch (OperationCanceledException)
        {
            row.Error = _text["video.canceled"];
            _activity.Add(ActivityLevel.Warning, _text.Format("video.canceled.log", row.Title), _text.Format("video.canceled.log", ActivityPlaceholders.Title));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or Core.Speech.SpeechException)
        {
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
    }

    /// <summary>The settings a queue item was stored with (only its subtitles are read).</summary>
    private static VideoOptions Stored(QueueItem item)
    {
        try
        {
            return JsonSerializer.Deserialize(item.SettingsJson, Core.Render.CoreJsonContext.Default.VideoOptions) ?? new VideoOptions();
        }
        catch (JsonException)
        {
            return new VideoOptions();
        }
    }

    /// <summary>Where the scene images come from for a saved images choice: a model, the sample images, or none.</summary>
    private static string ImageSourceOf(StepModelChoice? choice) =>
        !StepModelResolver.IsOff(choice) ? ImageSources.Model
        : choice is not null && StepModelResolver.Options(choice).ImageSource == ImageSources.Samples ? ImageSources.Samples
        : ImageSources.None;

    /// <summary>
    /// A made video: the last line of the page, one notification with the cost and the problems of the video (silent, no images…; they are
    /// also badges and notes on the Produced videos page), every line in the log file.
    /// </summary>
    private void Report(VideoRecord record)
    {
        var warnings = record.Warnings.Split(',', StringSplitOptions.RemoveEmptyEntries);
        var problems = new List<string>();
        foreach (var (code, key, level) in Problems)
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

    /// <summary>The warnings of a made video that the notification names (warning code, text key, log level).</summary>
    private static readonly (string Code, string Key, ActivityLevel Level)[] Problems =
    [
        (VideoWarnings.SpeechOff, "video.silent.off", ActivityLevel.Warning),
        (VideoWarnings.SpeechFailed, "video.silent.failed", ActivityLevel.Error),
        (VideoWarnings.SpeechSkipped, "video.speech.skipped", ActivityLevel.Warning),
        (VideoWarnings.ImagesFailed, "video.images.failed", ActivityLevel.Warning),
        (VideoWarnings.SamplesNone, "video.samples.none", ActivityLevel.Warning),
    ];

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

    /// <summary>What a queue item keeps: its subtitles choice (everything else is the profile's choice when the item starts).</summary>
    private VideoOptions Options() => new() { Subtitles = Subtitles };

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
        var estimate = _estimator.Estimate(queued, profile, engine, code, Picker.Model(PipelineSteps.Images), Picker.Model(PipelineSteps.Speech), Subtitles);
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
