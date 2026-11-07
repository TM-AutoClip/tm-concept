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
using TMConcept.Desktop.Localization;
using TMConcept.Desktop.Startup;

namespace TMConcept.Desktop.ViewModels;

/// <summary>
/// 🎬 Concept → video: approved concepts (left), the queue (middle; click moves a concept between the two, ↑↓ orders the queue) and the
/// latest videos (right). The models are chosen right here (the selected profile's last choices are shown): the video code model with its
/// effort and the engine, images (off, the profile's sample images, an online or a local image model) and narration (off, a local or an
/// online voice); subtitles and the narration file are set per queue item. The estimate of the queue follows the choices; after a video the
/// exact cost is shown. "Produce the queue in order" first makes the choices ready (licenses, an API key asked for in a dialog when a
/// provider has none, ONE confirmation that lists every download: ffmpeg, the engine, a local voice or image model) and saves them for the
/// profile; declined or failed = nothing is made or paid for. The videos are made one by one: before each item the queue is read again, so
/// a removed item is not made, a new one is, and the order follows ↑↓. A failed item stays in the queue with its error and can be retried;
/// cancel stops after the running step. Rows keep their progress across reloads.
/// </summary>
public sealed partial class VideoViewModel(AppSession session, IConceptStore concepts, IQueueStore queue, IVideoStore videos, IProfileStore profiles,
    VideoPipeline pipeline, IEnumerable<IVideoEngine> engines, IModelCatalog catalog, StepModelResolver resolver, Core.Media.IMediaTools media,
    VideoCostEstimator estimator, StepPickerViewModel picker, ConceptEvents events, INotifier notifier, IActivityLog activity, Localizer text,
    ILogger<VideoViewModel> logger) : PageViewModelBase
{
    private readonly IReadOnlyList<IVideoEngine> _engines = engines.ToList();
    private CancellationTokenSource? _cancel;
    private Guid? _running;
    private bool _attached;
    private bool _loading;

    /// <inheritdoc />
    public override string TitleKey => "nav.video";

    /// <inheritdoc />
    public override string Glyph => "🎬";

    /// <inheritdoc />
    public override int Order => 20;

    /// <summary>The models of this page: video code + engine, images, narration (the profile's last choices).</summary>
    public StepPickerViewModel Picker { get; } = picker;

    /// <summary>Estimate of the queued videos with the chosen models.</summary>
    [ObservableProperty]
    public partial string Estimate { get; set; } = string.Empty;

    /// <summary>Exact cost of the last video made.</summary>
    [ObservableProperty]
    public partial string LastCost { get; set; } = string.Empty;

    /// <summary>Price table date.</summary>
    [ObservableProperty]
    public partial string PricedOn { get; set; } = string.Empty;

    /// <summary>Subtitles in the picture.</summary>
    [ObservableProperty]
    public partial bool Subtitles { get; set; } = true;

    /// <summary>Approved concepts.</summary>
    public ObservableCollection<ConceptCardViewModel> Approved { get; } = [];

    /// <summary>The queue.</summary>
    public ObservableCollection<QueueRowViewModel> Queue { get; } = [];

    /// <summary>Latest videos.</summary>
    public ObservableCollection<string> Produced { get; } = [];

    /// <summary>Running.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Idle))]
    public partial bool Busy { get; set; }

    /// <summary>Not running.</summary>
    public bool Idle => !Busy;

    /// <inheritdoc />
    public override async Task ActivateAsync()
    {
        if (!_attached)
        {
            _attached = true;
            events.Changed += async (sender, _) =>
            {
                if (!ReferenceEquals(sender, this) && !Busy)
                {
                    try
                    {
                        await ReloadAsync().ConfigureAwait(true);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        logger.LogError(ex, "The video page could not be reloaded");
                    }
                }
            };
            Picker.Changed += async (_, _) =>
            {
                if (_loading)
                {
                    return;
                }

                try
                {
                    await EstimateAsync().ConfigureAwait(true);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogError(ex, "The video estimate could not be updated");
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
            LastCost = string.Empty;
            await LoadModelsAsync().ConfigureAwait(true);
            await ReloadAsync().ConfigureAwait(true);
        }
    }

    /// <inheritdoc />
    public override void CancelWork() => Cancel();

    /// <summary>Moves an approved concept into the queue with the current settings.</summary>
    [RelayCommand]
    public async Task EnqueueAsync(ConceptCardViewModel? card)
    {
        if (card is null || session.Profile is not { } profile)
        {
            return;
        }

        var options = Options();
        await queue.AddAsync(new QueueItem
        {
            ProfileId = profile.Id,
            ConceptId = card.Id,
            Position = Queue.Count,
            SettingsJson = JsonSerializer.Serialize(options, Core.Render.CoreJsonContext.Default.VideoOptions),
        }, CancellationToken.None).ConfigureAwait(true);
        await concepts.SetStatusAsync([card.Id], ConceptStatus.Queued, CancellationToken.None).ConfigureAwait(true);
        events.Raise(this);
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

        await queue.RemoveAsync(row.Item.Id, CancellationToken.None).ConfigureAwait(true);
        await concepts.SetStatusAsync([row.Item.ConceptId], ConceptStatus.Approved, CancellationToken.None).ConfigureAwait(true);
        events.Raise(this);
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

        if (session.Profile is not { } profile)
        {
            Status = text["picker.no_profile"];
            return;
        }

        Busy = true;
        using var cancel = new CancellationTokenSource();
        _cancel = cancel;
        var tried = new HashSet<Guid>();
        try
        {
            var items = await queue.ListAsync(profile.Id, CancellationToken.None).ConfigureAwait(true);
            if (!await PrepareAsync(items, cancel.Token).ConfigureAwait(true))
            {
                return;
            }

            // The queue is read again before every item: ✕, ↑↓ and new items during the run count; a failed item is tried once per press.
            while (!cancel.IsCancellationRequested)
            {
                var next = (await queue.ListAsync(profile.Id, CancellationToken.None).ConfigureAwait(true)).FirstOrDefault(i => !tried.Contains(i.Id));
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
            logger.LogInformation("Producing the queue was cancelled before an item started");
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
            logger.LogInformation("The retry was cancelled before the item started");
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

    partial void OnBusyChanged(bool value) => Working = value;

    partial void OnSubtitlesChanged(bool value) => _ = SafeEstimateAsync();

    private async Task LoadModelsAsync()
    {
        _loading = true;
        try
        {
            var profile = session.Profile;
            var language = profile?.ContentLanguage ?? (text.Language == "tr" ? ContentLanguages.Turkish : ContentLanguages.English);
            await Picker.LoadAsync(profile?.Id, language, PipelineSteps.VideoPage).ConfigureAwait(true);
            PricedOn = PriceInfo.Text(catalog, text);
        }
        finally
        {
            _loading = false;
        }
    }

    /// <summary>
    /// Makes the page's choices ready for the given queue items and saves them for the profile: the code model is required; the image model
    /// and the voice only when chosen; ffmpeg and the engine always (one confirmation for every download). Declined, failed or cancelled:
    /// the items get the reason and nothing is made.
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
            ready = await Picker.PrepareAsync(needed, "install.purpose.video", cancellationToken, media.RequiredComponents).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("Videos not started: cancelled while preparing");
            ready = false;
        }

        if (ready)
        {
            Status = string.Empty;
            return true;
        }

        var reason = Picker.Status.Length > 0 ? Picker.Status : text["install.cancelled"];
        logger.LogWarning("Videos not started: the chosen models are not ready");
        foreach (var row in Queue.Where(r => items.Any(i => i.Id == r.Item.Id)))
        {
            row.Error = text["video.error.missing"] + " " + reason;
        }

        Status = reason;
        activity.Add(ActivityLevel.Warning, text["video.error.missing"] + " " + reason);
        return false;
    }

    private async Task ProduceAsync(QueueRowViewModel row, CancellationToken cancellationToken)
    {
        row.Error = string.Empty;
        row.Busy = true;
        row.Percent = 0;
        _running = row.Item.Id;
        try
        {
            // The item may have been removed (✕) after the run started: then it is not made and costs nothing.
            var stored = (await queue.ListAsync(row.Item.ProfileId, CancellationToken.None).ConfigureAwait(true)).FirstOrDefault(i => i.Id == row.Item.Id);
            var concept = stored is null ? null : await concepts.GetAsync(row.Item.ConceptId, CancellationToken.None).ConfigureAwait(true);
            if (stored is null || concept is not { Status: ConceptStatus.Queued })
            {
                logger.LogInformation("Queue item skipped: it was removed or its concept is no longer queued");
                return;
            }

            if (await profiles.GetAsync(row.Item.ProfileId, CancellationToken.None).ConfigureAwait(true) is not { } profile)
            {
                row.Error = text["video.error.concept"];
                return;
            }

            var options = JsonSerializer.Deserialize(stored.SettingsJson, Core.Render.CoreJsonContext.Default.VideoOptions) ?? Options();

            // The models are the profile's choices, saved when the run was prepared: code model and engine, images, narration.
            ResolvedModel model;
            var choice = await resolver.ChoiceAsync(PipelineSteps.VideoCode, profile.Id, CancellationToken.None).ConfigureAwait(true);
            try
            {
                model = await resolver.ResolveAsync(PipelineSteps.VideoCode, choice, CancellationToken.None).ConfigureAwait(true);
            }
            catch (ModelNotReadyException ex)
            {
                logger.LogWarning("Video not started: the {Step} step has no {Missing}", ex.Step, ex.Missing);
                row.Error = ex.Missing == "key" ? text.Format("concepts.no_key", choice?.Provider ?? string.Empty) : text["video.no_model"];
                return;
            }

            options.Engine = StepModelResolver.Options(choice).Engine;
            options.CodeProvider = model.Model.Provider;
            options.CodeModel = model.Model.Id;
            options.Images = ImageSourceOf(await resolver.ChoiceAsync(PipelineSteps.Images, profile.Id, CancellationToken.None).ConfigureAwait(true));
            options.Speech = !StepModelResolver.IsOff(await resolver.ChoiceAsync(PipelineSteps.Speech, profile.Id, CancellationToken.None).ConfigureAwait(true));

            var progress = new Progress<VideoProgress>(p =>
            {
                row.Percent = Math.Round(p.Share * 100);
                row.Step = text["video.step." + p.Step] + (p.Detail.Length > 0 ? " · " + p.Detail : string.Empty);
            });
            var record = await pipeline.RunAsync(new VideoJob(concept, profile, options, model), progress, cancellationToken).ConfigureAwait(true);
            await queue.RemoveAsync(row.Item.Id, CancellationToken.None).ConfigureAwait(true);
            Report(record);
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
            LastCost = text.Format("video.last_cost.failed", text.Usd(ex.SpentUsd));
            activity.Add(ActivityLevel.Error, text.Format("video.failed", row.Title, reason, text.Usd(ex.SpentUsd)),
                text.Format("video.failed", ActivityPlaceholders.Title, reason, text.Usd(ex.SpentUsd)));
            notifier.Notify(new AppNotification(NotificationKind.Failed, text["video.failed.title"], row.Title));
        }
        catch (OperationCanceledException)
        {
            row.Error = text["video.canceled"];
            activity.Add(ActivityLevel.Warning, text.Format("video.canceled.log", row.Title), text.Format("video.canceled.log", ActivityPlaceholders.Title));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or Core.Speech.SpeechException)
        {
            logger.LogError(ex, "Video failed");
            row.Error = ex.Message;
            activity.Add(ActivityLevel.Error, text.Format("video.failed", row.Title, ex.Message, text.Usd(0)), text.Format("video.failed", ActivityPlaceholders.Title, ex.Message, text.Usd(0)));
        }
        finally
        {
            row.Busy = false;
            _running = null;

            // The job's buffers (scene images, audio, model answers) are garbage now: give the memory back (small machines).
            Memory.MemoryRelease.Now(logger, "a video job");
        }
    }

    /// <summary>Where the scene images come from for a saved images choice: a model, the sample images, or none.</summary>
    private static string ImageSourceOf(StepModelChoice? choice) =>
        !StepModelResolver.IsOff(choice) ? ImageSources.Model
        : choice is not null && StepModelResolver.Options(choice).ImageSource == ImageSources.Samples ? ImageSources.Samples
        : ImageSources.None;

    private void Report(VideoRecord record)
    {
        var warnings = record.Warnings.Split(',', StringSplitOptions.RemoveEmptyEntries);
        if (warnings.Contains(VideoWarnings.SpeechOff))
        {
            activity.Add(ActivityLevel.Warning, text.Format("video.silent.off", record.Title), text.Format("video.silent.off", ActivityPlaceholders.Title));
        }

        if (warnings.Contains(VideoWarnings.SpeechFailed))
        {
            activity.Add(ActivityLevel.Error, text.Format("video.silent.failed", record.Title), text.Format("video.silent.failed", ActivityPlaceholders.Title));
        }

        if (warnings.Contains(VideoWarnings.SpeechSkipped))
        {
            activity.Add(ActivityLevel.Warning, text.Format("video.speech.skipped", record.Title), text.Format("video.speech.skipped", ActivityPlaceholders.Title));
        }

        if (warnings.Contains(VideoWarnings.ImagesFailed))
        {
            activity.Add(ActivityLevel.Warning, text.Format("video.images.failed", record.Title), text.Format("video.images.failed", ActivityPlaceholders.Title));
        }

        if (warnings.Contains(VideoWarnings.SamplesNone))
        {
            activity.Add(ActivityLevel.Warning, text.Format("video.samples.none", record.Title), text.Format("video.samples.none", ActivityPlaceholders.Title));
        }

        var seconds = (record.ElapsedMs / 1000.0).ToString("0", CultureInfo.InvariantCulture);
        LastCost = text.Format(record.CostExact ? "video.last_cost" : "video.last_cost.approx", text.Usd(record.TotalUsd));
        activity.Add(ActivityLevel.Info, text.Format("video.done", record.Title, text.Usd(record.TotalUsd), seconds),
            text.Format("video.done", ActivityPlaceholders.Title, text.Usd(record.TotalUsd), seconds));
        notifier.Notify(new AppNotification(NotificationKind.Done, text["video.done.title"], record.Title));
        events.Raise(this);
    }

    // The provider's code of an answer cut at its output limit.
    private const string Truncated = "truncated";

    /// <summary>
    /// Why a video failed. A cut-off answer here is the video code (the image prompts fall back to the scene texts): the shared provider text
    /// speaks of concepts kept before the cut, which means nothing for a video, so it has its own text with what was paid.
    /// </summary>
    private string Reason(string code, string detail, decimal spentUsd)
    {
        if (!code.StartsWith(VideoException.ProviderPrefix, StringComparison.Ordinal))
        {
            return text["video.error." + code] + " " + detail;
        }

        var provider = code[VideoException.ProviderPrefix.Length..];
        return provider == Truncated ? text.Format("video.error.truncated", text.Usd(spentUsd)) : ProviderErrors.Text(text, provider);
    }

    /// <summary>The row's error: the last failure and what the failed tries of this item paid together ("spent because of the failure").</summary>
    private string FailureText(QueueItem item)
    {
        var reason = Reason(item.LastErrorCode, item.LastErrorDetail, item.FailedUsd);
        return item.FailedUsd <= 0 || item.LastErrorCode == VideoException.ProviderPrefix + Truncated
            ? reason
            : reason + " " + text.Format("video.failed.spent", text.Usd(item.FailedUsd));
    }

    private async Task KeepFailureAsync(QueueItem item, VideoException failure)
    {
        try
        {
            await queue.RecordFailureAsync(item.Id, failure.Code, failure.Message, failure.SpentUsd, CancellationToken.None).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The row shows it anyway; only after a restart it would be gone.
            logger.LogError(ex, "The failure of a queue item could not be kept");
        }
    }

    private VideoOptions Options() => new()
    {
        Images = Picker.Row(PipelineSteps.Images)?.Selected?.Kind switch
        {
            StepOptionKind.Model => ImageSources.Model,
            StepOptionKind.Samples => ImageSources.Samples,
            _ => ImageSources.None,
        },
        Speech = Picker.Model(PipelineSteps.Speech) is not null,
        Subtitles = Subtitles,
    };

    private async Task MoveAsync(QueueRowViewModel? row, int step)
    {
        if (row is null || session.Profile is not { } profile)
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
        await queue.ReorderAsync(profile.Id, Queue.Select(r => r.Item.Id).ToList(), CancellationToken.None).ConfigureAwait(true);
    }

    /// <summary>The estimate of the queued videos with the models chosen now (or why there is none).</summary>
    private async Task EstimateAsync()
    {
        if (session.Profile is not { } profile)
        {
            Estimate = text["concepts.estimate.no_profile"];
            return;
        }

        if (Picker.Model(PipelineSteps.VideoCode) is not { } code)
        {
            Estimate = text["video.estimate.none"];
            return;
        }

        var queued = new List<Concept>();
        foreach (var row in Queue)
        {
            if (await concepts.GetAsync(row.Item.ConceptId, CancellationToken.None).ConfigureAwait(true) is { } concept)
            {
                queued.Add(concept);
            }
        }

        if (queued.Count == 0)
        {
            Estimate = text["video.estimate.empty"];
            return;
        }

        var engine = _engines.FirstOrDefault(e => e.Kind == Picker.Row(PipelineSteps.VideoCode)?.Engine?.Kind);
        var estimate = estimator.Estimate(queued, profile, engine, code, Picker.Model(PipelineSteps.Images), Picker.Model(PipelineSteps.Speech), Subtitles);
        Estimate = text.Format("video.estimate", queued.Count, text.Usd(estimate.MinUsd), text.Usd(estimate.MaxUsd));
    }

    private async Task SafeEstimateAsync()
    {
        try
        {
            await EstimateAsync().ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "The video estimate could not be updated");
        }
    }

    /// <summary>
    /// Reads the lists, then rebuilds them in one go (no await between clearing and filling, so two reloads cannot mix). A queue row that
    /// is still in the queue keeps its object: the running item keeps its progress and stays locked, a failed one keeps its error.
    /// </summary>
    private async Task ReloadAsync()
    {
        if (session.Profile is not { } profile)
        {
            Approved.Clear();
            Produced.Clear();
            Queue.Clear();
            Badge = string.Empty;
            await EstimateAsync().ConfigureAwait(true);
            return;
        }

        var approved = await concepts.ListAsync(profile.Id, [ConceptStatus.Approved], false, CancellationToken.None).ConfigureAwait(true);
        var queued = (await concepts.ListAsync(profile.Id, [ConceptStatus.Queued], false, CancellationToken.None).ConfigureAwait(true)).ToDictionary(c => c.Id);
        var items = await queue.ListAsync(profile.Id, CancellationToken.None).ConfigureAwait(true);
        var produced = (await videos.ListAsync(profile.Id, CancellationToken.None).ConfigureAwait(true)).Take(30).Select(v => v.Title).ToList();
        if (session.Profile?.Id != profile.Id)
        {
            return;
        }

        var cards = Approved.ToDictionary(c => c.Id);
        CollectionSync.Apply(Approved, approved.Select(c => cards.TryGetValue(c.Id, out var card) ? card : new ConceptCardViewModel(c, text)).ToList());
        var rows = Queue.ToDictionary(r => r.Item.Id);
        CollectionSync.Apply(Queue, items.Select(item => rows.TryGetValue(item.Id, out var row) ? row : new QueueRowViewModel(item,
            queued.TryGetValue(item.ConceptId, out var concept) ? "#" + concept.Number.ToString(CultureInfo.InvariantCulture) + " " + concept.Title : "?")
        {
            Error = item.LastErrorCode.Length > 0 ? FailureText(item) : string.Empty,
        }).ToList());

        Produced.Clear();
        foreach (var title in produced)
        {
            Produced.Add(title);
        }

        Badge = Queue.Count == 0 ? string.Empty : Queue.Count.ToString(CultureInfo.InvariantCulture);
        await EstimateAsync().ConfigureAwait(true);
    }
}
