using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using TMConcept.Core.Activity;
using TMConcept.Core.Models;
using TMConcept.Core.Notifications;
using TMConcept.Core.Pipeline;
using TMConcept.Core.Profiles;
using TMConcept.Core.Speech;
using TMConcept.Core.Videos;
using TMConcept.Desktop.Dialogs;
using TMConcept.Desktop.Localization;

namespace TMConcept.Desktop.ViewModels;

/// <summary>
/// Notes on a made video while it plays (F16, 0.1.49; under the player of the Produced videos page). Writing starts: the video stops and the
/// note belongs to that moment and, by the version's own timing, to the scene playing then. The notes are listed and kept in the version
/// folder (<see cref="VideoFolders.NotesFile"/>, JSON; the database is not changed). "Fix" sends all of them in ONE call of the profile's
/// video code model (<see cref="SceneFixer"/>) and makes a new version where only the noted scenes change (KURALLAR 12); its estimate is
/// shown and confirmed first (a local voice and local images cost nothing).
/// </summary>
public sealed partial class TimeNotesViewModel(VideoRemaker remaker, SceneFixer fixer, StepModelResolver resolver, SpeechService speech,
    IConfirmDialog confirm, INotifier notifier, IActivityLog activity, Localizer text, ProductionProgressViewModel progress, ILogger<TimeNotesViewModel> logger)
    : ObservableObject
{
    private VersionSource? _source;
    private Profile? _profile;
    private string _folder = string.Empty;
    private CancellationTokenSource? _cancel;

    /// <summary>A fixed version was made (the page shows it).</summary>
    public event EventHandler<VideoRecord>? Made;

    /// <summary>The notes of the shown version, in time order.</summary>
    public ObservableCollection<TimeNoteRowViewModel> Notes { get; } = [];

    /// <summary>The note being written.</summary>
    [ObservableProperty]
    public partial string Draft { get; set; } = string.Empty;

    /// <summary>The moment the note being written belongs to (set when writing starts; milliseconds).</summary>
    [ObservableProperty]
    public partial long? PendingMs { get; set; }

    /// <summary>The shown version can take notes (its timing was kept).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanFix))]
    public partial bool Ready { get; set; }

    /// <summary>A fixed version is being made.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanFix))]
    public partial bool Busy { get; set; }

    /// <summary>"Fix" can run: notes, a version to start from, nothing running.</summary>
    public bool CanFix => Ready && !Busy && Notes.Count > 0;

    /// <summary>What the last action did, or why nothing can be done.</summary>
    [ObservableProperty]
    public partial string Status { get; set; } = string.Empty;

    /// <summary>The steps of the fixed version being made.</summary>
    public ProductionProgressViewModel Progress { get; } = progress;

    /// <summary>Starts from <paramref name="record"/> of <paramref name="profile"/>: its timing and its kept notes (read off the window's thread).</summary>
    public async Task LoadAsync(VideoRecord? record, Profile? profile)
    {
        Notes.Clear();
        Draft = string.Empty;
        PendingMs = null;
        Status = string.Empty;
        Ready = false;
        _source = null;
        _profile = profile;
        _folder = string.Empty;
        OnPropertyChanged(nameof(CanFix));
        if (record is null || profile is null)
        {
            return;
        }

        try
        {
            var (source, notes) = await Task.Run(async () =>
            {
                var loaded = remaker.Load(record);
                return (loaded, await TimeNotes.ReadAsync(loaded.Folder, CancellationToken.None).ConfigureAwait(false));
            }).ConfigureAwait(true);
            _source = source;
            _folder = source.Folder;
            foreach (var note in notes.OrderBy(n => n.AtMs))
            {
                Notes.Add(Row(note));
            }

            Ready = true;
        }
        catch (Exception ex) when (ex is VideoException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            logger.LogInformation("A version cannot take notes: {Reason}", ex.Message);
            Status = text["notes.no_source"];
        }

        OnPropertyChanged(nameof(CanFix));
    }

    /// <summary>Writing started at <paramref name="positionMs"/> of the video (the view has stopped it): the note belongs to that moment.</summary>
    public void Begin(long positionMs) => PendingMs ??= Math.Max(0, positionMs);

    /// <summary>Adds the note being written at its moment and keeps the notes in the version folder.</summary>
    [RelayCommand]
    public async Task AddAsync()
    {
        if (_source is not { } source || Draft.Trim().Length == 0)
        {
            return;
        }

        var at = PendingMs ?? 0;
        var note = new TimeNote { AtMs = at, Scene = TimeNotes.SceneAt(source.Input, at), Text = Draft.Trim() };
        var index = Notes.TakeWhile(n => n.Note.AtMs <= at).Count();
        Notes.Insert(index, Row(note));
        Draft = string.Empty;
        PendingMs = null;
        OnPropertyChanged(nameof(CanFix));
        await SaveAsync().ConfigureAwait(true);
    }

    /// <summary>Removes a note.</summary>
    [RelayCommand]
    public async Task RemoveAsync(TimeNoteRowViewModel? row)
    {
        if (row is null || Busy || !Notes.Remove(row))
        {
            return;
        }

        OnPropertyChanged(nameof(CanFix));
        await SaveAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// "Fix": the profile's video code model (and voice and image model when the version had them) made ready, the estimate confirmed, then
    /// the new version made with only the noted scenes changed.
    /// </summary>
    [RelayCommand]
    public async Task FixAsync()
    {
        if (!CanFix || _source is not { } source || _profile is not { } profile)
        {
            return;
        }

        Status = string.Empty;
        ResolvedModel code;
        ResolvedModel? image = null;
        ResolvedModel? voiceModel = null;
        StepModelChoice? voiceChoice = null;
        try
        {
            code = await resolver.ResolveAsync(PipelineSteps.VideoCode, await resolver.ChoiceAsync(PipelineSteps.VideoCode, profile.Id, CancellationToken.None)
                .ConfigureAwait(true), CancellationToken.None).ConfigureAwait(true);
            if (source.Record.ImageModel.Length > 0 && await resolver.ChoiceAsync(PipelineSteps.Images, profile.Id, CancellationToken.None).ConfigureAwait(true) is { } imageChoice
                && !StepModelResolver.IsOff(imageChoice))
            {
                image = await resolver.ResolveAsync(PipelineSteps.Images, imageChoice, CancellationToken.None).ConfigureAwait(true);
            }

            // A version that had a voice gets the profile's voice for its changed scenes (a silent one stays silent).
            if (source.Record.SpeechModel.Length > 0 && await resolver.ChoiceAsync(PipelineSteps.Speech, profile.Id, CancellationToken.None).ConfigureAwait(true) is { } choice
                && !StepModelResolver.IsOff(choice))
            {
                voiceChoice = choice;
                voiceModel = await resolver.ResolveAsync(PipelineSteps.Speech, choice, CancellationToken.None).ConfigureAwait(true);
            }
        }
        catch (ModelNotReadyException ex)
        {
            logger.LogWarning("Fix not started: the {Step} step has no {Missing}", ex.Step, ex.Missing);
            Status = text["notes.models"];
            return;
        }

        var notes = TimeNotes.ByScene(Notes.Select(n => n.Note));
        var estimate = fixer.Estimate(source, notes, code.Model, image?.Model, voiceModel?.Model);
        var answer = await confirm.AskAsync(text["notes.fix.title"], text.Format("notes.fix.confirm", notes.Count, code.Model.Name, text.Usd(estimate.MinUsd),
            text.Usd(estimate.MaxUsd)), text["notes.fix"], danger: false).ConfigureAwait(true);
        if (!answer.Confirmed)
        {
            return;
        }

        Busy = true;
        using var cancel = new CancellationTokenSource();
        _cancel = cancel;
        var made = false;
        try
        {
            Progress.Begin("work.version", code.Model.Name);
            SpeechSetup? voice = null;
            if (voiceChoice is not null)
            {
                voice = await speech.SetupAsync(voiceChoice, profile.ContentLanguage, cancel.Token).ConfigureAwait(true);
            }

            VideoRecord record;
            try
            {
                record = await fixer.FixAsync(new FixJob(source, profile, notes, code, voice, image), Progress.Reporter, cancel.Token).ConfigureAwait(true);
                made = true;
            }
            finally
            {
                if (voice is not null)
                {
                    await speech.ReleaseAsync(voice, CancellationToken.None).ConfigureAwait(true);
                }
            }

            var name = VersionEditorViewModel.VersionName(record);
            Status = text.Format("notes.fix.done", name, text.Usd(record.TotalUsd));
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
        catch (Exception ex) when (ex is ModelNotReadyException or SpeechException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            logger.LogWarning(ex, "A fixed version could not start");
            Status = text.Format("version.failed", ex.Message);
            activity.Add(ActivityLevel.Error, Status);
            notifier.Notify(new AppNotification(NotificationKind.Failed, text["version.failed.title"], Status));
        }
        finally
        {
            await Progress.FinishAsync(made).ConfigureAwait(true);
            _cancel = null;
            Busy = false;
        }
    }

    /// <summary>Stops the fixed version after the running step.</summary>
    [RelayCommand]
    public void Cancel() => _cancel?.Cancel();

    private TimeNoteRowViewModel Row(TimeNote note) =>
        new(note, text.Format("notes.line", TimeSpan.FromMilliseconds(note.AtMs).ToString(@"m\:ss", text.Culture), note.Scene));

    /// <summary>Keeps the notes in the version folder (off the window's thread; a failure is said, the list stays).</summary>
    private async Task SaveAsync()
    {
        if (_folder.Length == 0)
        {
            return;
        }

        var notes = Notes.Select(n => n.Note).ToList();
        var folder = _folder;
        try
        {
            await Task.Run(() => TimeNotes.WriteAsync(folder, notes, CancellationToken.None)).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "The notes of a version could not be kept");
            Status = text.Format("notes.save_failed", ex.Message);
        }
    }
}
