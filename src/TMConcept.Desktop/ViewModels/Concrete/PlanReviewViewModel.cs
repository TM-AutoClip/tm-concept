using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using TMConcept.Core.Activity;
using TMConcept.Core.Notifications;
using TMConcept.Core.Videos;
using TMConcept.Desktop.Localization;

namespace TMConcept.Desktop.ViewModels;

/// <summary>
/// The plan preview on the video page (F10, 0.1.49; <see cref="IPlanReview"/>): while a video waits for its plan, the sheet of still frames
/// (placeholders instead of the scene images) and one note box per scene show; "Approve" goes on, "Plan again with the notes" sends all the
/// notes in one new plan call, "Reject" stops the video before its images are paid for. With "one scene first" the first scene drawn with its
/// real image shows the same way (approve or reject). The wait never holds the window's thread: the pipeline awaits a task the buttons
/// complete. Night mode switched on during a wait approves it (nobody is at the computer); Cancel of the queue ends the wait as a cancel.
/// </summary>
public sealed partial class PlanReviewViewModel(Localizer text, IActivityLog activity, INotifier notifier, ILogger<PlanReviewViewModel> logger)
    : ObservableObject, IPlanReview
{
    private TaskCompletionSource<PlanReviewAnswer>? _waiting;
    private Func<bool> _nightMode = () => false;

    /// <summary>The scenes with their note boxes.</summary>
    public ObservableCollection<SceneNoteRowViewModel> Scenes { get; } = [];

    /// <summary>A video waits for an answer (the panel shows).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSheet), nameof(CanRevise))]
    public partial bool Waiting { get; set; }

    /// <summary>The first scene with its real image is shown (no notes, approve or reject only).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRevise))]
    public partial bool FirstScene { get; set; }

    /// <summary>The sheet of still frames (a PNG; empty when it could not be made).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSheet))]
    public partial string Sheet { get; set; } = string.Empty;

    /// <summary>The panel's title ("The plan of «Greek fire» (round 2)").</summary>
    [ObservableProperty]
    public partial string Title { get; set; } = string.Empty;

    /// <summary>Whether the sheet is shown.</summary>
    public bool HasSheet => Waiting && Sheet.Length > 0;

    /// <summary>Notes can be sent (a plan, not the first scene).</summary>
    public bool CanRevise => Waiting && !FirstScene;

    /// <inheritdoc />
    public bool NightMode => _nightMode();

    /// <summary>The page's night mode checkbox (read before every wait).</summary>
    public void Attach(Func<bool> nightMode) => _nightMode = nightMode ?? throw new ArgumentNullException(nameof(nightMode));

    /// <summary>The title of the video whose plan is shown next.</summary>
    public string VideoTitle { get; set; } = string.Empty;

    /// <inheritdoc />
    public async Task<PlanReviewAnswer> PlanAsync(PlanPreview preview, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(preview);
        return await WaitAsync(preview, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<bool> FirstSceneAsync(PlanPreview preview, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(preview);
        return (await WaitAsync(preview, cancellationToken).ConfigureAwait(false)).Decision != PlanReviewDecision.Reject;
    }

    /// <summary>Goes on with the plan (or the first scene) as it is.</summary>
    [RelayCommand]
    public void Approve() => Answer(PlanReviewAnswer.Approve);

    /// <summary>Plans again with every note (one call); without a note it is the same as approving.</summary>
    [RelayCommand]
    public void Revise()
    {
        if (FirstScene)
        {
            return;
        }

        var notes = Scenes.Where(s => s.Note.Trim().Length > 0).Select(s => new SceneNote(s.Number, s.Note.Trim())).ToList();
        Answer(notes.Count == 0 ? PlanReviewAnswer.Approve : new PlanReviewAnswer(PlanReviewDecision.Revise, notes));
    }

    /// <summary>Stops the video; its images are never paid for.</summary>
    [RelayCommand]
    public void Reject() => Answer(PlanReviewAnswer.Reject);

    /// <summary>Night mode was switched on: a waiting video goes on (nobody is at the computer to answer).</summary>
    public void NightModeOn()
    {
        if (Waiting)
        {
            logger.LogInformation("Night mode was switched on while a plan waited; it is approved");
            Answer(PlanReviewAnswer.Approve);
        }
    }

    private async Task<PlanReviewAnswer> WaitAsync(PlanPreview preview, CancellationToken cancellationToken)
    {
        var wait = new TaskCompletionSource<PlanReviewAnswer>(TaskCreationOptions.RunContinuationsAsynchronously);
        await Dispatcher.UIThread.InvokeAsync(() => Show(preview, wait));
        await using var registration = cancellationToken.Register(() => wait.TrySetCanceled(cancellationToken)).ConfigureAwait(false);
        try
        {
            return await wait.Task.ConfigureAwait(false);
        }
        finally
        {
            await Dispatcher.UIThread.InvokeAsync(() => Hide(wait));
        }
    }

    private void Show(PlanPreview preview, TaskCompletionSource<PlanReviewAnswer> wait)
    {
        _waiting = wait;
        Scenes.Clear();
        foreach (var scene in preview.Scenes)
        {
            var at = TimeSpan.FromSeconds(scene.StartSeconds).ToString(@"m\:ss", text.Culture);
            var kind = text.Has("video.review.base." + scene.Base) ? text["video.review.base." + scene.Base] : scene.Base;
            Scenes.Add(new SceneNoteRowViewModel(scene.Number, text.Format("video.review.scene", scene.Number, kind, at), scene.Caption));
        }

        Sheet = preview.Sheet;
        FirstScene = preview.FirstScene;
        Title = preview.FirstScene ? text.Format("video.review.first.title", VideoTitle)
            : preview.Round > 1 ? text.Format("video.review.title.again", VideoTitle, preview.Round)
            : text.Format("video.review.title", VideoTitle);
        Waiting = true;
        var line = preview.FirstScene ? text.Format("video.review.first.waits", VideoTitle) : text.Format("video.review.waits", VideoTitle);
        activity.Add(ActivityLevel.Info, line, preview.FirstScene
            ? text.Format("video.review.first.waits", ActivityPlaceholders.Title)
            : text.Format("video.review.waits", ActivityPlaceholders.Title));
        notifier.Notify(new AppNotification(NotificationKind.Warning, text["video.review.notify"], line));

        // Night mode switched on while the preview was being drawn (before this wait began): nobody is there to answer, it goes on.
        if (NightMode)
        {
            NightModeOn();
        }
    }

    private void Hide(TaskCompletionSource<PlanReviewAnswer> wait)
    {
        if (!ReferenceEquals(_waiting, wait))
        {
            return;
        }

        _waiting = null;
        Waiting = false;
        FirstScene = false;
        Sheet = string.Empty;
        Scenes.Clear();
    }

    private void Answer(PlanReviewAnswer answer)
    {
        if (_waiting is not { } wait)
        {
            return;
        }

        logger.LogInformation("The previewed plan was answered: {Decision} ({Notes} notes)", answer.Decision, answer.Notes.Count);
        wait.TrySetResult(answer);
    }
}
