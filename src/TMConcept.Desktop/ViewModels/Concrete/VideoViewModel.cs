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
using TMConcept.Core.Speech;
using TMConcept.Core.Videos;
using TMConcept.Desktop.Localization;
using TMConcept.Desktop.Startup;

namespace TMConcept.Desktop.ViewModels;

/// <summary>
/// 🎬 Concept → video: approved concepts (left), the queue (middle; click moves a concept between the two, ↑↓ orders the queue) and the
/// latest videos (right). The engine, the code model, the image model and the voice come from the profile's models (its override, then the
/// general default; a line shows what will be used); the page sets images on/off (or the profile's sample images), narration, subtitles
/// and the narration file per queue item. Before the first item, anything the settings need and that is missing (ffmpeg, the engine,
/// the local voice or image model) is installed after one confirmation; declined = nothing is made. "Produce the queue in order" makes the
/// videos one by one (one job at a time): before each item the queue is read again, so a removed item is not made, a new one is, and the
/// order follows ↑↓; each item is made with its own profile. A failed item stays in the queue with its error and can be retried; cancel
/// stops after the running step. Rows keep their progress across reloads (coming back to the page, adding to the queue).
/// </summary>
public sealed partial class VideoViewModel(AppSession session, IConceptStore concepts, IQueueStore queue, IVideoStore videos, IProfileStore profiles,
    VideoPipeline pipeline, IEnumerable<IVideoEngine> engines, IModelCatalog catalog, StepModelResolver resolver, Core.Media.IMediaTools media,
    Components.ComponentInstallFlow installs, ConceptEvents events, INotifier notifier, IActivityLog activity, Localizer text, ILogger<VideoViewModel> logger)
    : PageViewModelBase
{
    private readonly IReadOnlyList<IVideoEngine> _engines = engines.ToList();
    private CancellationTokenSource? _cancel;
    private Guid? _running;
    private bool _attached;

    /// <inheritdoc />
    public override string TitleKey => "nav.video";

    /// <inheritdoc />
    public override string Glyph => "🎬";

    /// <inheritdoc />
    public override int Order => 20;

    /// <summary>What the selected profile's videos are made with (engine, code model, images, voice).</summary>
    [ObservableProperty]
    public partial string Summary { get; set; } = string.Empty;

    /// <summary>Note when the profile's engine (or ffmpeg) is not installed yet: it is installed after a confirmation when producing.</summary>
    [ObservableProperty]
    public partial string EngineNote { get; set; } = string.Empty;

    /// <summary>Install progress 0–100 (before the first video).</summary>
    [ObservableProperty]
    public partial double InstallPercent { get; set; }

    /// <summary>What is being installed.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Installing))]
    public partial string InstallText { get; set; } = string.Empty;

    /// <summary>Whether an install runs.</summary>
    public bool Installing => InstallText.Length > 0;

    /// <summary>Image choices.</summary>
    public IReadOnlyList<ChoiceOption> ImageChoices { get; } =
    [
        new(ImageSources.None, text["video.images.none"]), new(ImageSources.Samples, text["video.images.samples"]), new(ImageSources.Model, text["video.images.model"]),
    ];

    /// <summary>Chosen image source.</summary>
    [ObservableProperty]
    public partial ChoiceOption? Images { get; set; }

    /// <summary>Narration.</summary>
    [ObservableProperty]
    public partial bool Speech { get; set; } = true;

    /// <summary>Subtitles in the picture.</summary>
    [ObservableProperty]
    public partial bool Subtitles { get; set; } = true;

    /// <summary>Narration text file.</summary>
    [ObservableProperty]
    public partial bool NarrationFile { get; set; } = true;

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
        }

        Images ??= ImageChoices.First(c => c.Value == ImageSources.Model);
        await SummaryAsync().ConfigureAwait(true);
        await ReloadAsync().ConfigureAwait(true);
    }

    /// <inheritdoc />
    public override async Task ProfileChangedAsync()
    {
        if (!Busy)
        {
            await SummaryAsync().ConfigureAwait(true);
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
            return;
        }

        Busy = true;
        using var cancel = new CancellationTokenSource();
        _cancel = cancel;
        var tried = new HashSet<Guid>();
        try
        {
            if (!await EnsureComponentsAsync(profile, cancel.Token).ConfigureAwait(true))
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
            if (session.Profile is { } profile && !await EnsureComponentsAsync(profile, cancel.Token).ConfigureAwait(true))
            {
                return;
            }

            await ProduceAsync(row, cancel.Token).ConfigureAwait(true);
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

    /// <summary>The summary line and the engine note of the selected profile.</summary>
    private async Task SummaryAsync()
    {
        if (session.Profile is not { } profile)
        {
            Summary = string.Empty;
            EngineNote = string.Empty;
            return;
        }

        var code = await resolver.ChoiceAsync(PipelineSteps.VideoCode, profile.Id, CancellationToken.None).ConfigureAwait(true);
        var images = await resolver.ChoiceAsync(PipelineSteps.Images, profile.Id, CancellationToken.None).ConfigureAwait(true);
        var voice = await resolver.ChoiceAsync(PipelineSteps.Speech, profile.Id, CancellationToken.None).ConfigureAwait(true);
        var engine = EngineOf(code);
        string Name(StepModelChoice? choice, string off) =>
            StepModelResolver.IsOff(choice) ? off : catalog.Find(choice!.Provider, choice.Model)?.Name ?? choice.Model;
        Summary = text.Format("video.summary", engine?.Name ?? "-", Name(code, text["models.none_short"]), Name(images, text["models.off_short"]),
            Name(voice, text["models.off_short"]));
        var status = engine?.Status();
        EngineNote = engine is null || status!.Ready ? string.Empty : text.Format("video.engine.missing", engine.Name, string.Join(", ", status.Missing));
    }

    private IVideoEngine? EngineOf(StepModelChoice? code) =>
        _engines.FirstOrDefault(e => e.Kind == StepModelResolver.Options(code).Engine) ?? _engines.FirstOrDefault();

    /// <summary>
    /// Installs what the profile's settings need before the first item (one confirmation): ffmpeg, the engine, the local voice and image
    /// model. Declined or failed: every queued item gets the reason and nothing is made.
    /// </summary>
    private async Task<bool> EnsureComponentsAsync(Profile profile, CancellationToken cancellationToken)
    {
        var codes = new List<string>(media.RequiredComponents);
        var code = await resolver.ChoiceAsync(PipelineSteps.VideoCode, profile.Id, cancellationToken).ConfigureAwait(true);
        if (EngineOf(code) is { } engine)
        {
            codes.Add(Engines.Components.EngineComponent.CodeOf(engine.Kind));
        }

        foreach (var step in new[] { PipelineSteps.Speech, PipelineSteps.Images })
        {
            var choice = await resolver.ChoiceAsync(step, profile.Id, cancellationToken).ConfigureAwait(true);
            if (!StepModelResolver.IsOff(choice) && catalog.Find(choice!.Provider, choice.Model) is { } model)
            {
                codes.AddRange(model.Requires);
            }
        }

        var progress = new Progress<Core.Tools.ToolProgress>(p =>
        {
            InstallPercent = Math.Round(p.Share * 100);
            InstallText = p.Share >= 1 ? string.Empty : text.Format("video.installing", p.Text);
        });
        var outcome = await installs.EnsureAsync(codes.Distinct(StringComparer.Ordinal).ToList(), "install.purpose.video", progress, cancellationToken)
            .ConfigureAwait(true);
        InstallText = string.Empty;
        await SummaryAsync().ConfigureAwait(true);
        if (outcome.Ready)
        {
            return true;
        }

        logger.LogWarning("Videos not started: what the settings need is not installed");
        foreach (var row in Queue)
        {
            row.Error = text["video.error.missing"] + " " + outcome.Reason;
        }

        activity.Add(ActivityLevel.Warning, text["video.error.missing"] + " " + outcome.Reason);
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

            // Each item is made with its own profile, whatever profile is selected now.
            if (await profiles.GetAsync(row.Item.ProfileId, CancellationToken.None).ConfigureAwait(true) is not { } profile)
            {
                row.Error = text["video.error.concept"];
                return;
            }

            var options = JsonSerializer.Deserialize(stored.SettingsJson, Core.Render.CoreJsonContext.Default.VideoOptions) ?? Options();

            // The code model and the engine are the profile's (override → general default) at the time the item is made.
            ResolvedModel model;
            var choice = await resolver.ChoiceAsync(PipelineSteps.VideoCode, profile.Id, CancellationToken.None).ConfigureAwait(true);
            try
            {
                model = await resolver.ResolveAsync(PipelineSteps.VideoCode, choice ?? new StepModelChoice(), CancellationToken.None).ConfigureAwait(true);
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
            // The pipeline wrote the cause (with its step and code); the activity line below goes to the log too.
            row.Error = Reason(ex);
            activity.Add(ActivityLevel.Error, text.Format("video.failed", row.Title, row.Error, text.Usd(ex.SpentUsd)),
                text.Format("video.failed", ActivityPlaceholders.Title, row.Error, text.Usd(ex.SpentUsd)));
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

        if (warnings.Contains(VideoWarnings.ImagesFailed))
        {
            activity.Add(ActivityLevel.Warning, text.Format("video.images.failed", record.Title), text.Format("video.images.failed", ActivityPlaceholders.Title));
        }

        var seconds = (record.ElapsedMs / 1000.0).ToString("0", CultureInfo.InvariantCulture);
        activity.Add(ActivityLevel.Info, text.Format("video.done", record.Title, text.Usd(record.TotalUsd), seconds),
            text.Format("video.done", ActivityPlaceholders.Title, text.Usd(record.TotalUsd), seconds));
        notifier.Notify(new AppNotification(NotificationKind.Done, text["video.done.title"], record.Title));
        events.Raise(this);
    }

    private string Reason(VideoException ex) => ex.Code.StartsWith("provider:", StringComparison.Ordinal)
        ? ProviderErrors.Text(text, ex.Code["provider:".Length..])
        : text["video.error." + ex.Code] + " " + ex.Message;

    private VideoOptions Options() => new()
    {
        Images = Images?.Value ?? ImageSources.Model,
        Speech = Speech,
        Subtitles = Subtitles,
        NarrationFile = NarrationFile,
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
            queued.TryGetValue(item.ConceptId, out var concept) ? "#" + concept.Number.ToString(CultureInfo.InvariantCulture) + " " + concept.Title : "?")).ToList());

        Produced.Clear();
        foreach (var title in produced)
        {
            Produced.Add(title);
        }

        Badge = Queue.Count == 0 ? string.Empty : Queue.Count.ToString(CultureInfo.InvariantCulture);
    }
}
