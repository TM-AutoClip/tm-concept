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
using TMConcept.Core.Speech;
using TMConcept.Core.Videos;
using TMConcept.Desktop.Localization;
using TMConcept.Desktop.Startup;

namespace TMConcept.Desktop.ViewModels;

/// <summary>
/// 🎬 Concept → video: approved concepts (left), the queue (middle; click moves a concept between the two, ↑↓ orders the queue) and the
/// latest videos (right). Settings: engine, code model, images, narration, subtitles, narration file; they are stored with each queue
/// item. "Produce the queue in order" makes the videos one by one (one job at a time); a failed item stays in the queue with its error and
/// can be retried; cancel stops after the running step.
/// </summary>
public sealed partial class VideoViewModel(AppSession session, IConceptStore concepts, IQueueStore queue, IVideoStore videos, VideoPipeline pipeline,
    IEnumerable<IVideoEngine> engines, IModelCatalog catalog, IStepModelStore steps, StepModelResolver resolver, ConceptEvents events, INotifier notifier,
    IActivityLog activity, Localizer text, ILogger<VideoViewModel> logger) : PageViewModelBase
{
    private CancellationTokenSource? _cancel;
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
        Speech = session.Profile?.SpeechEngine is { Length: > 0 };
        await LoadModelsAsync().ConfigureAwait(true);
        await ReloadAsync().ConfigureAwait(true);
    }

    /// <inheritdoc />
    public override Task ProfileChangedAsync() => Busy ? Task.CompletedTask : ReloadAsync();

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

    /// <summary>Moves a queued concept back to the approved list.</summary>
    [RelayCommand]
    public async Task DequeueAsync(QueueRowViewModel? row)
    {
        if (row is null || row.Busy)
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

        Busy = true;
        using var cancel = new CancellationTokenSource();
        _cancel = cancel;
        try
        {
            foreach (var row in Queue.ToList())
            {
                if (cancel.IsCancellationRequested)
                {
                    break;
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

    private async Task ProduceAsync(QueueRowViewModel row, CancellationToken cancellationToken)
    {
        row.Error = string.Empty;
        row.Busy = true;
        row.Percent = 0;
        try
        {
            if (session.Profile is not { } profile || await concepts.GetAsync(row.Item.ConceptId, CancellationToken.None).ConfigureAwait(true) is not { } concept)
            {
                row.Error = text["video.error.concept"];
                return;
            }

            var options = JsonSerializer.Deserialize(row.Item.SettingsJson, Core.Render.CoreJsonContext.Default.VideoOptions) ?? Options();
            ResolvedModel model;
            try
            {
                model = await resolver.ResolveAsync(PipelineSteps.VideoCode, new StepModelChoice
                {
                    Step = PipelineSteps.VideoCode,
                    Provider = options.CodeProvider,
                    Model = options.CodeModel,
                    OptionsJson = (await steps.GetAsync(PipelineSteps.VideoCode, CancellationToken.None).ConfigureAwait(true))?.OptionsJson ?? "{}",
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

    private async Task ReloadAsync()
    {
        Approved.Clear();
        Produced.Clear();
        var errors = Queue.Where(r => r.Failed).ToDictionary(r => r.Item.Id, r => r.Error);
        Queue.Clear();
        if (session.Profile is not { } profile)
        {
            return;
        }

        foreach (var concept in await concepts.ListAsync(profile.Id, [ConceptStatus.Approved], false, CancellationToken.None).ConfigureAwait(true))
        {
            Approved.Add(new ConceptCardViewModel(concept, text));
        }

        var queued = (await concepts.ListAsync(profile.Id, [ConceptStatus.Queued], false, CancellationToken.None).ConfigureAwait(true)).ToDictionary(c => c.Id);
        foreach (var item in await queue.ListAsync(profile.Id, CancellationToken.None).ConfigureAwait(true))
        {
            var title = queued.TryGetValue(item.ConceptId, out var concept) ? "#" + concept.Number.ToString(CultureInfo.InvariantCulture) + " " + concept.Title : "?";
            Queue.Add(new QueueRowViewModel(item, title) { Error = errors.GetValueOrDefault(item.Id, string.Empty) });
        }

        foreach (var video in (await videos.ListAsync(profile.Id, CancellationToken.None).ConfigureAwait(true)).Take(30))
        {
            Produced.Add(video.Title);
        }

        Badge = Queue.Count == 0 ? string.Empty : Queue.Count.ToString(CultureInfo.InvariantCulture);
    }
}
