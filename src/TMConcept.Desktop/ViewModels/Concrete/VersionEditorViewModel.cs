using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using TMConcept.Core.Activity;
using TMConcept.Core.Engines;
using TMConcept.Core.Markers;
using TMConcept.Core.Models;
using TMConcept.Core.Notifications;
using TMConcept.Core.Pipeline;
using TMConcept.Core.Profiles;
using TMConcept.Core.Speech;
using TMConcept.Core.Videos;
using TMConcept.Desktop.Dialogs;
using TMConcept.Desktop.Localization;
using TMConcept.Engines.Components;

namespace TMConcept.Desktop.ViewModels;

/// <summary>
/// "New version" of a produced video (KURALLAR 12): starts from the chosen version. (a) The narration of each scene can be edited and
/// extended, with placeholders (pauses, speed, the transition into the scene; all applied by this computer), and a voice chosen: the voice
/// is made again here, the scenes are timed to the new clips, the kept code is rendered again and a fade is added by ffmpeg — no AI cost; an
/// online voice's cost is shown first.
/// (b) Another engine needs new code: the estimate of the code call is shown and confirmed first; images, clips and texts are used again.
/// Both make a new version folder; older versions stay. The models come from the profile's choices (video code with its engine, voice) and
/// are made ready like any process (keys, licenses, the install window); the choices are saved for the profile. While a version is made,
/// its steps show with their time and cost (<see cref="Progress"/>, the same list as a video in the queue).
/// </summary>
public sealed partial class VersionEditorViewModel(StepPickerViewModel picker, VideoRemaker remaker, SpeechService speech, StepModelResolver resolver,
    VideoCostEstimator estimator, IEnumerable<IVideoEngine> engines, Core.Media.IMediaTools media, IConfirmDialog confirm, INotifier notifier,
    IActivityLog activity, Localizer text, ProductionProgressViewModel progress, ILogger<VersionEditorViewModel> logger) : ObservableObject
{
    private static readonly IReadOnlyList<string> Steps = [PipelineSteps.VideoCode, PipelineSteps.Speech];

    private readonly IReadOnlyList<IVideoEngine> _engines = engines.ToList();
    private VersionSource? _source;
    private Profile? _profile;
    private CancellationTokenSource? _cancel;
    private bool _attached;

    /// <summary>A new version was made (the page shows it).</summary>
    public event EventHandler<VideoRecord>? Made;

    /// <summary>The models: video code with its engine (for another engine) and the voice (for a new voice), two compact cards.</summary>
    public StepPickerViewModel Picker { get; } = Compact(picker);

    /// <summary>The scenes with their narration to edit.</summary>
    public ObservableCollection<SceneEditViewModel> Scenes { get; } = [];

    /// <summary>
    /// The placeholders of the drop-downs: pauses and speed for the voice, the transition into the scene; all applied on this computer (a new
    /// version is voiced and rendered again here, no AI cost).
    /// </summary>
    public IReadOnlyList<MarkerOption> Placeholders => MarkerOptions.For(text);

    /// <summary>Whether the chosen version can be the start of a new one (its project and input were kept).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanStart))]
    public partial bool CanEdit { get; set; }

    /// <summary>A new version is being made.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanStart))]
    public partial bool Busy { get; set; }

    /// <summary>Whether a new version can be started now.</summary>
    public bool CanStart => CanEdit && !Busy;

    /// <summary>The steps of the version being made.</summary>
    public ProductionProgressViewModel Progress { get; } = progress;

    /// <summary>Why nothing can be made, or what the last attempt did.</summary>
    [ObservableProperty]
    public partial string Status { get; set; } = string.Empty;

    /// <summary>What a new voice costs (an online voice) or that it is free.</summary>
    [ObservableProperty]
    public partial string VoiceCost { get; set; } = string.Empty;

    /// <summary>The estimate of new code for the chosen engine.</summary>
    [ObservableProperty]
    public partial string RecodeEstimate { get; set; } = string.Empty;

    /// <summary>Starts from <paramref name="record"/> of <paramref name="profile"/>: the scenes, the profile's model choices, the estimates.</summary>
    public async Task LoadAsync(VideoRecord? record, Profile? profile)
    {
        Attach();
        Scenes.Clear();
        _source = null;
        _profile = profile;
        CanEdit = false;
        Status = string.Empty;
        if (record is null || profile is null)
        {
            return;
        }

        try
        {
            _source = remaker.Load(record);
        }
        catch (Exception ex) when (ex is VideoException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            logger.LogInformation("A version cannot be the start of a new one: {Reason}", ex.Message);
            Status = text["version.no_source"];
            return;
        }

        for (var i = 0; i < _source.Input.Scenes.Count; i++)
        {
            var scene = _source.Input.Scenes[i];
            var edit = new SceneEditViewModel(scene.Number, text.Format("produced.scene", scene.Number, scene.DurationSeconds.ToString("0.0", text.Culture)),
                scene.Visual, _source.Texts[i]);
            edit.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(SceneEditViewModel.Text))
                {
                    Estimate();
                }
            };
            Scenes.Add(edit);
        }

        await Picker.LoadAsync(profile.Id, profile.ContentLanguage, Steps).ConfigureAwait(true);
        CanEdit = true;
        Estimate();
    }

    /// <summary>
    /// (a) A new version with the edited narration and the chosen voice (off = silent), rendered here with the kept code. An online voice's
    /// cost is confirmed first.
    /// </summary>
    [RelayCommand]
    public async Task RevoiceAsync()
    {
        if (!CanStart || _source is not { } source || _profile is not { } profile)
        {
            return;
        }

        var model = Picker.Model(PipelineSteps.Speech);
        var cost = VideoCostEstimator.Speech(model, Characters());
        if (cost > 0)
        {
            var answer = await confirm.AskAsync(text["version.revoice.title"], text.Format("version.revoice.paid", model!.Name, text.Usd(cost)),
                text["version.revoice"], danger: false).ConfigureAwait(true);
            if (!answer.Confirmed)
            {
                return;
            }
        }

        await RunAsync(PipelineSteps.Speech, source.Engine, string.Empty, async (token, progress) =>
        {
            SpeechSetup? voice = null;
            if (Picker.Choice(PipelineSteps.Speech) is { } choice && model is not null)
            {
                voice = await speech.SetupAsync(choice, profile.ContentLanguage, token).ConfigureAwait(true);
            }

            return await remaker.RevoiceAsync(new RevoiceJob(source, profile, Scenes.Select(s => s.Text).ToList(), voice), progress, token).ConfigureAwait(true);
        }).ConfigureAwait(true);
    }

    /// <summary>(b) A new version for the engine chosen in the video code card: new code from the code model (paid; confirmed with its estimate first).</summary>
    [RelayCommand]
    public async Task RecodeAsync()
    {
        if (!CanStart || _source is not { } source || _profile is not { } profile)
        {
            return;
        }

        if (Picker.Model(PipelineSteps.VideoCode) is not { } code || TargetEngine() is not { } engine)
        {
            Status = text["video.no_model"];
            return;
        }

        var estimate = estimator.Code(source.Input, profile.ContentLanguage, engine, code, VideoRemaker.Options(source.Record).Subtitles);
        var answer = await confirm.AskAsync(text["version.recode.title"], text.Format("version.recode.confirm", engine.Name, code.Name, text.Usd(estimate.MinUsd),
            text.Usd(estimate.MaxUsd)), text["version.recode"], danger: false).ConfigureAwait(true);
        if (!answer.Confirmed)
        {
            return;
        }

        await RunAsync(PipelineSteps.VideoCode, engine, code.Name, async (token, progress) =>
        {
            var model = await resolver.ResolveAsync(PipelineSteps.VideoCode, Picker.Choice(PipelineSteps.VideoCode), token).ConfigureAwait(true);
            return await remaker.RecodeAsync(new RecodeJob(source, profile, engine, model), progress, token).ConfigureAwait(true);
        }).ConfigureAwait(true);
    }

    /// <summary>Stops after the running step (nothing is kept of the unfinished version).</summary>
    [RelayCommand]
    public void Cancel() => _cancel?.Cancel();

    private static StepPickerViewModel Compact(StepPickerViewModel picker)
    {
        picker.Columns = 2;
        picker.Compact = true;
        return picker;
    }

    private void Attach()
    {
        if (_attached)
        {
            return;
        }

        _attached = true;
        Picker.Changed += (_, _) => Estimate();
    }

    /// <summary>
    /// Makes the step ready (key, license, installs of the voice, ffmpeg and the engine in the install window; the choice is saved for the
    /// profile), then makes the version with its steps shown (<paramref name="codeModel"/> names the code call's model; empty = no code call);
    /// the result is shown, said in a notification and written to the log file.
    /// </summary>
    private async Task RunAsync(string step, IVideoEngine engine, string codeModel,
        Func<CancellationToken, IProgress<VideoProgress>, Task<VideoRecord>> make)
    {
        Busy = true;
        Status = string.Empty;
        using var cancel = new CancellationTokenSource();
        _cancel = cancel;
        try
        {
            var needed = step == PipelineSteps.Speech && Picker.Model(step) is null ? Array.Empty<string>() : [step];
            var components = media.RequiredComponents.Append(EngineComponent.CodeOf(engine.Kind)).ToList();
            if (!await Picker.PrepareAsync(needed, "install.purpose.version", cancel.Token, components).ConfigureAwait(true))
            {
                Status = Picker.Status.Length > 0 ? Picker.Status : text["install.cancelled"];
                return;
            }

            Progress.Begin("work.version", codeModel);
            VideoRecord record;
            var made = false;
            try
            {
                record = await make(cancel.Token, Progress.Reporter).ConfigureAwait(true);
                made = true;
            }
            finally
            {
                await Progress.FinishAsync(made).ConfigureAwait(true);
            }

            var name = VersionName(record);
            Status = text.Format("version.done", name, text.Usd(record.TotalUsd));
            activity.Add(ActivityLevel.Info, Status);
            notifier.Notify(new AppNotification(NotificationKind.Done, text["version.done.title"], record.Title + " · " + Status));
            Made?.Invoke(this, record);
        }
        catch (VideoException ex)
        {
            var reason = VideoFailures.Reason(text, ex.Code, ex.Message, ex.SpentUsd);
            Status = ex.SpentUsd > 0 && !VideoFailures.IsTruncated(ex.Code) ? reason + " " + text.Format("video.failed.spent", text.Usd(ex.SpentUsd)) : reason;
            activity.Add(ActivityLevel.Error, text.Format("version.failed", Status));
            notifier.Notify(new AppNotification(NotificationKind.Failed, text["version.failed.title"], Status));
        }
        catch (OperationCanceledException)
        {
            Status = text["video.canceled"];
            activity.Add(ActivityLevel.Warning, text["version.canceled"]);
        }
        catch (Exception ex) when (ex is ModelNotReadyException or SpeechException)
        {
            logger.LogWarning(ex, "A new version could not start");
            Status = text.Format("version.failed", ex.Message);
        }
        finally
        {
            _cancel = null;
            Busy = false;
        }
    }

    /// <summary>The version folder's name of a record ("v2").</summary>
    public static string VersionName(VideoRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        var folder = record.Folder.Replace('\\', '/').TrimEnd('/');
        return folder[(folder.LastIndexOf('/') + 1)..];
    }

    private IVideoEngine? TargetEngine() => _engines.FirstOrDefault(e => e.Kind == Picker.Row(PipelineSteps.VideoCode)?.Engine?.Kind);

    /// <summary>The characters a new voice speaks (markers are not spoken).</summary>
    private long Characters() => Scenes.Sum(s => (long)MarkerText.Spoken(s.Text).Length);

    private void Estimate()
    {
        if (_source is not { } source || _profile is not { } profile)
        {
            VoiceCost = string.Empty;
            RecodeEstimate = string.Empty;
            return;
        }

        var voice = Picker.Model(PipelineSteps.Speech);
        var cost = VideoCostEstimator.Speech(voice, Characters());
        VoiceCost = voice is null ? text["version.voice.off"]
            : cost > 0 ? text.Format("version.voice.paid", voice.Name, text.Usd(cost))
            : text.Format("version.voice.free", voice.Name);
        if (Picker.Model(PipelineSteps.VideoCode) is not { } code || TargetEngine() is not { } engine)
        {
            RecodeEstimate = text["video.estimate.none"];
            return;
        }

        var estimate = estimator.Code(source.Input, profile.ContentLanguage, engine, code, VideoRemaker.Options(source.Record).Subtitles);
        RecodeEstimate = text.Format("version.recode.estimate", engine.Name, code.Name, text.Usd(estimate.MinUsd), text.Usd(estimate.MaxUsd))
            + (engine.Kind == source.Engine.Kind ? " " + text["version.recode.same"] : string.Empty);
    }
}
