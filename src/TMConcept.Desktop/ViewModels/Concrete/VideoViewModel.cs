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
/// latest videos (right). Settings: engine, code model, images, narration, subtitles, narration file; they are stored with each queue
/// item (an item stored without a code model takes the page's choice, then the saved default). "Produce the queue in order" makes the
/// videos one by one (one job at a time): before each item the queue is read again, so a removed item is not made, a new one is, and the
/// order follows ↑↓; each item is made with its own profile. A failed item stays in the queue with its error and can be retried; cancel
/// stops after the running step. Rows keep their progress across reloads (coming back to the page, adding to the queue).
/// </summary>
public sealed partial class VideoViewModel(AppSession session, IConceptStore concepts, IQueueStore queue, IVideoStore videos, IProfileStore profiles,
    VideoPipeline pipeline, IEnumerable<IVideoEngine> engines, IModelCatalog catalog, IStepModelStore steps, StepModelResolver resolver, ConceptEvents events,
    INotifier notifier, IActivityLog activity, Localizer text, ILogger<VideoViewModel> logger) : PageViewModelBase
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

    /// <summary>Engines.</summary>
    public IReadOnlyList<EngineOption> Engines { get; } = engines.OrderBy(e => e.Kind)
        .Select(e => new EngineOption(e.Kind, e.Name + (e.Experimental ? " · " + text["video.experimental"] : e.NeedsLicenseConsent ? " · " + text["video.license"] : string.Empty)))
        .ToList();

    /// <summary>Chosen engine.</summary>
    [ObservableProperty]
    public partial EngineOption? Engine { get; set; }

    /// <summary>Red note when the chosen engine (or ffmpeg) is not installed yet; empty when it is ready.</summary>
    [ObservableProperty]
    public partial string EngineNote { get; set; } = string.Empty;

    /// <summary>Code models.</summary>
    public ObservableCollection<ModelOption> Models { get; } = [];

    /// <summary>Chosen code model.</summary>
    [ObservableProperty]
    public partial ModelOption? CodeModel { get; set; }

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

        Engine ??= Engines.FirstOrDefault();
        Images ??= ImageChoices[0];
        if (!Busy)
        {
            Speech = session.Profile?.SpeechEngine is { Length: > 0 };
        }

        RefreshEngineNote();
        await LoadModelsAsync().ConfigureAwait(true);
        await ReloadAsync().ConfigureAwait(true);
    }

    /// <inheritdoc />
    public override Task ProfileChangedAsync() => Busy ? Task.CompletedTask : ReloadAsync();

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

    partial void OnEngineChanged(EngineOption? value) => RefreshEngineNote();

    private void RefreshEngineNote()
    {
        if (Engine is null || _engines.FirstOrDefault(e => e.Kind == Engine.Kind) is not { } engine)
        {
            EngineNote = string.Empty;
            return;
        }

        var status = engine.Status();
        EngineNote = status.Ready ? string.Empty : text.Format("video.engine.missing", engine.Name, string.Join(", ", status.Missing));
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
            if (options.CodeProvider.Length == 0 && CodeModel?.Model is { } chosen)
            {
                // Queued before a code model was chosen: the page's current choice is used.
                options.CodeProvider = chosen.Provider;
                options.CodeModel = chosen.Id;
            }

            ResolvedModel model;
            try
            {
                var saved = await steps.GetAsync(PipelineSteps.VideoCode, CancellationToken.None).ConfigureAwait(true);
                model = await resolver.ResolveAsync(PipelineSteps.VideoCode, options.CodeProvider.Length == 0 ? null : new StepModelChoice
                {
                    Step = PipelineSteps.VideoCode,
                    Provider = options.CodeProvider,
                    Model = options.CodeModel,
                    OptionsJson = saved?.OptionsJson ?? "{}",
                }, CancellationToken.None).ConfigureAwait(true);
            }
            catch (ModelNotReadyException ex)
            {
                row.Error = ex.Missing == "key" ? text.Format("concepts.no_key", options.CodeProvider) : text["video.no_model"];
                return;
            }

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
            row.Error = Reason(ex);
            activity.Add(ActivityLevel.Error, text.Format("video.failed", row.Title, row.Error, text.Usd(ex.SpentUsd)));
            notifier.Notify(new AppNotification(NotificationKind.Failed, text["video.failed.title"], row.Title));
        }
        catch (OperationCanceledException)
        {
            row.Error = text["video.canceled"];
            activity.Add(ActivityLevel.Warning, text.Format("video.canceled.log", row.Title));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or Core.Speech.SpeechException)
        {
            logger.LogError(ex, "Video failed");
            row.Error = ex.Message;
            activity.Add(ActivityLevel.Error, text.Format("video.failed", row.Title, ex.Message, text.Usd(0)));
        }
        finally
        {
            row.Busy = false;
            _running = null;
        }
    }

    private void Report(VideoRecord record)
    {
        var warnings = record.Warnings.Split(',', StringSplitOptions.RemoveEmptyEntries);
        if (warnings.Contains(VideoWarnings.SpeechOff))
        {
            activity.Add(ActivityLevel.Warning, text.Format("video.silent.off", record.Title));
        }

        if (warnings.Contains(VideoWarnings.SpeechFailed))
        {
            activity.Add(ActivityLevel.Error, text.Format("video.silent.failed", record.Title));
        }

        if (warnings.Contains(VideoWarnings.ImagesFailed))
        {
            activity.Add(ActivityLevel.Warning, text.Format("video.images.failed", record.Title));
        }

        var seconds = (record.ElapsedMs / 1000.0).ToString("0", CultureInfo.InvariantCulture);
        activity.Add(ActivityLevel.Info, text.Format("video.done", record.Title, text.Usd(record.TotalUsd), seconds));
        notifier.Notify(new AppNotification(NotificationKind.Done, text["video.done.title"], record.Title));
        events.Raise(this);
    }

    private string Reason(VideoException ex) => ex.Code.StartsWith("provider:", StringComparison.Ordinal)
        ? ProviderErrors.Text(text, ex.Code["provider:".Length..])
        : text["video.error." + ex.Code] + " " + ex.Message;

    private VideoOptions Options() => new()
    {
        Engine = Engine?.Kind ?? EngineKind.HyperFrames,
        CodeProvider = CodeModel?.Model?.Provider ?? string.Empty,
        CodeModel = CodeModel?.Model?.Id ?? string.Empty,
        Images = Images?.Value ?? ImageSources.None,
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

    private async Task LoadModelsAsync()
    {
        var current = CodeModel?.Model;
        Models.Clear();
        Models.Add(new ModelOption(null, text["models.none"]));
        foreach (var model in catalog.With(ModelCapability.Chat).OrderBy(m => m.Provider, StringComparer.Ordinal).ThenBy(m => m.Name, StringComparer.Ordinal))
        {
            Models.Add(new ModelOption(model, model.Name + " · " + model.Provider));
        }

        var saved = await steps.GetAsync(PipelineSteps.VideoCode, CancellationToken.None).ConfigureAwait(true);
        CodeModel = Models.FirstOrDefault(o => o.Model is { } m && current is not null && m.Provider == current.Provider && m.Id == current.Id)
            ?? Models.FirstOrDefault(o => o.Model is { } m && saved is not null && m.Provider == saved.Provider && m.Id == saved.Model)
            ?? Models[0];
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
