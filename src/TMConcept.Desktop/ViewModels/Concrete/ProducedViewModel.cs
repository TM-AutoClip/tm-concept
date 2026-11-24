using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using TMConcept.Core.Activity;
using TMConcept.Core.Concepts;
using TMConcept.Core.Media;
using TMConcept.Core.Text;
using TMConcept.Core.Videos;
using TMConcept.Desktop.Dialogs;
using TMConcept.Desktop.Localization;
using TMConcept.Desktop.Startup;

namespace TMConcept.Desktop.ViewModels;

/// <summary>
/// 🎞 Produced videos: one row per video (its newest version): thumbnail (the first frame that is not black), title, date, length, cost and
/// warning badges (a file deleted from outside shows "file missing"). The details show the versions of the video (v1, v2…; choose one to
/// watch it, delete one); the in-app player (a chosen video does not start by itself) with "open in the system player" beside it; the
/// whole narration scene by scene, the whole concept text, settings and cost per step; "New version" (a new voice for an edited narration
/// rendered here with the same code, or new code for another engine: <see cref="VersionEditorViewModel"/>); open the folder; produce
/// again (the concept goes back to the approved list of the video page; the new video is a new row); delete the version (0.1.28: for good
/// with its files, after a confirmation that shows the size). A chosen version whose thumbnail file is missing gets it made again in the background (the
/// same rule: the first frame that is not black), so the player and the list do not show a black area.
/// </summary>
public sealed partial class ProducedViewModel(AppSession session, IVideoStore videos, IConceptStore concepts, Videos.VideoRows rows, Videos.VideoFiles files,
    Core.Deletion.VideoDeletion deletion, IConfirmDialog confirm, ConceptEvents events, Concepts.ConceptActions actions, IActivityLog activity, PlayerViewModel player,
    IMediaTools media, Localizer text, VersionEditorViewModel editor, TimeNotesViewModel notes, Core.Runs.IRunStore runs, ILogger<ProducedViewModel> logger)
    : PageViewModelBase
{
    private readonly List<VideoRowViewModel> _all = [];
    private Guid? _select;
    private readonly HashSet<Guid> _thumbnailing = [];
    private bool _attached;

    /// <inheritdoc />
    public override string TitleKey => "nav.produced";

    /// <inheritdoc />
    public override int Order => 30;

    /// <summary>Videos (filtered): the newest version of each.</summary>
    public ObservableCollection<VideoRowViewModel> Items { get; } = [];

    /// <summary>The versions of the selected video, oldest first.</summary>
    public ObservableCollection<VideoRowViewModel> Versions { get; } = [];

    /// <summary>The version shown (the player, the texts, the details, the start of a new version).</summary>
    [ObservableProperty]
    public partial VideoRowViewModel? SelectedVersion { get; set; }

    /// <summary>"New version": a new voice or another engine for the shown version.</summary>
    public VersionEditorViewModel Editor { get; } = editor;

    /// <summary>F16 (0.1.49): the notes on the shown version under the player, and "Fix" (a new version where only the noted scenes change).</summary>
    public TimeNotesViewModel Notes { get; } = notes;

    /// <summary>Search.</summary>
    [ObservableProperty]
    public partial string Search { get; set; } = string.Empty;

    /// <summary>Selected video.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    public partial VideoRowViewModel? Selected { get; set; }

    /// <summary>Whether a video is selected.</summary>
    public bool HasSelection => Selected is not null;

    /// <summary>Details of the selected video.</summary>
    [ObservableProperty]
    public partial string Details { get; set; } = string.Empty;

    /// <summary>The concept the selected video was made from (title, summary, the whole script) and the code prompt version.</summary>
    [ObservableProperty]
    public partial string ConceptText { get; set; } = string.Empty;

    /// <summary>
    /// The narration of the selected video, scene by scene and complete (from its version folder: <c>source/input.json</c>, the timing and
    /// texts it was rendered with).
    /// </summary>
    public ObservableCollection<SceneText> Scenes { get; } = [];

    /// <summary>Why there are no scenes to show (empty when there are).</summary>
    [ObservableProperty]
    public partial string ScenesNote { get; set; } = string.Empty;

    /// <summary>The player.</summary>
    public PlayerViewModel Player { get; } = player;

    /// <inheritdoc />
    public override void Deactivate() => Player.Close();

    /// <summary>The background thumbnail work of the chosen video (tests wait for it); a finished task when there is none.</summary>
    internal Task ThumbnailWork { get; private set; } = Task.CompletedTask;

    /// <inheritdoc />
    public override Task ActivateAsync()
    {
        // The shared player shows this page's notes (the video page clears them).
        Player.Notes = Notes;
        if (!_attached)
        {
            _attached = true;
            Editor.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(VersionEditorViewModel.Busy))
                {
                    Working = Editor.Busy || Notes.Busy;
                }
            };
            Notes.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(TimeNotesViewModel.Busy))
                {
                    Working = Editor.Busy || Notes.Busy;

                    // Another version chosen while a fixed one was made: its notes are read now (a busy list is not reloaded).
                    if (!Notes.Busy && SelectedVersion?.Video.Id != Notes.VersionId)
                    {
                        NotesWork = NotesAsync(SelectedVersion);
                    }
                }
            };
            Notes.Made += async (_, record) =>
            {
                try
                {
                    _select = record.Id;
                    await ReloadAsync().ConfigureAwait(true);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogError(ex, "The video list could not be reloaded");
                }
            };
            Editor.Made += async (_, record) =>
            {
                try
                {
                    _select = record.Id;
                    await ReloadAsync().ConfigureAwait(true);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogError(ex, "The video list could not be reloaded");
                }
            };
            events.Changed += async (_, _) =>
            {
                try
                {
                    await ReloadAsync().ConfigureAwait(true);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogError(ex, "The video list could not be reloaded");
                }
            };
        }

        return ReloadAsync();
    }

    /// <inheritdoc />
    public override Task ProfileChangedAsync() => ReloadAsync();

    /// <inheritdoc />
    public override void CancelWork() => Editor.Cancel();

    /// <summary>Opens the selected video in the system's player.</summary>
    [RelayCommand]
    public void Play()
    {
        if ((SelectedVersion ?? Selected) is { } row)
        {
            Status = files.Play(row);
        }
    }

    /// <summary>Puts the concept of the selected video back on the video page (approved), so it can be made again.</summary>
    [RelayCommand]
    public async Task ProduceAgainAsync()
    {
        if (Selected is not { } row)
        {
            return;
        }

        try
        {
            var (ok, error) = await actions.ProduceAgainAsync(row.Video.ConceptId, this).ConfigureAwait(true);
            Status = ok ? text.Format("produced.again.done", row.Title) : text[error];
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "The concept could not be put back for production");
            Status = text.Format("error.generic", ex.Message);
        }
    }

    /// <summary>Opens the video folder.</summary>
    [RelayCommand]
    public void OpenFolder()
    {
        if ((SelectedVersion ?? Selected) is { } row)
        {
            Status = files.OpenFolder(row);
        }
    }

    /// <summary>
    /// Deletes the shown version for good after a confirmation that shows its size (0.1.28: its folder always goes, and its concept's folder
    /// when it was the last version; the other versions stay). The player lets go of the file first. A folder that cannot be removed keeps the
    /// record and the reason is shown. While work runs (a new version being made from a version, a video queue) nothing is deleted: the
    /// running work may read the files of the version (checked before the confirmation and again after it).
    /// </summary>
    [RelayCommand]
    public async Task DeleteAsync()
    {
        if ((SelectedVersion ?? Selected) is not { } row || RefuseWhileWorking())
        {
            return;
        }

        try
        {
            var plan = await deletion.PlanAsync([row.Video.Id], CancellationToken.None).ConfigureAwait(true);
            var size = StorageSettingsViewModel.Size(plan.Bytes, text.Culture);
            var answer = await confirm.AskAsync(text["produced.delete.title"], text.Format("produced.delete.message", row.Title, size), text["common.delete"],
                danger: true).ConfigureAwait(true);
            if (!answer.Confirmed || RefuseWhileWorking())
            {
                return;
            }

            if (ReferenceEquals(SelectedVersion, row) || ReferenceEquals(Selected, row))
            {
                // The player holds the file open on Windows: it lets go before the folder is deleted.
                Player.Unload();
            }

            var result = await deletion.DeleteAsync([row.Video.Id], CancellationToken.None).ConfigureAwait(true);
            if (!result.Complete)
            {
                var reason = result.Failed[0].Reason;
                logger.LogError(LogEvents.OnScreen, "The video could not be deleted; its record stays");
                Status = text.Format("produced.delete_failed", reason);
                activity.Add(ActivityLevel.Error, Status);
                await ReloadAsync().ConfigureAwait(true);
                return;
            }

            // Another version of the same video stays shown (the newest that is left).
            _select = Versions.LastOrDefault(v => !ReferenceEquals(v, row))?.Video.Id;
            var freed = StorageSettingsViewModel.Size(result.Freed, text.Culture);
            activity.Add(ActivityLevel.Info, text.Format("produced.deleted", row.Title, freed), text.Format("produced.deleted", ActivityPlaceholders.Title, freed));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "The video could not be deleted");
            Status = text.Format("produced.delete_failed", ex.Message);
            activity.Add(ActivityLevel.Error, Status);
        }

        await ReloadAsync().ConfigureAwait(true);
    }

    /// <summary>Whether a delete must wait: a new version or a video queue is running (the status says so).</summary>
    private bool RefuseWhileWorking()
    {
        if (!session.WorkRunning && !Editor.Busy)
        {
            return false;
        }

        Status = text["produced.delete.busy"];
        return true;
    }

    partial void OnSearchChanged(string value) => Filter();

    partial void OnSelectedChanged(VideoRowViewModel? value)
    {
        Versions.Clear();
        if (value is not null)
        {
            foreach (var version in _all.Where(r => r.GroupKey == value.GroupKey).OrderBy(r => r.Version ?? 0))
            {
                Versions.Add(version);
            }
        }

        SelectedVersion = value;
    }

    partial void OnSelectedVersionChanged(VideoRowViewModel? value)
    {
        Details = string.Empty;
        ConceptText = string.Empty;
        Status = string.Empty;
        Scenes.Clear();
        ScenesNote = string.Empty;
        if (value is null)
        {
            Player.Unload();
            NotesWork = NotesAsync(null);
            return;
        }

        Player.Load(value.File, value.Thumbnail, value.Video.Width, value.Video.Height, value.Video.Fps, value.Video.DurationMs);
        ThumbnailWork = ThumbnailAgainAsync(value);
        _ = DetailsAsync(value);
        _ = EditorAsync(value);
        NotesWork = NotesAsync(value);
    }

    /// <summary>The notes of the shown version being read (tests wait for it).</summary>
    internal Task NotesWork { get; private set; } = Task.CompletedTask;

    /// <summary>The notes start from the shown version (not while a fixed version is made).</summary>
    private async Task NotesAsync(VideoRowViewModel? version)
    {
        if (Notes.Busy)
        {
            return;
        }

        try
        {
            await Notes.LoadAsync(version?.Video, session.Profile).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "The notes of a version could not be loaded");
        }
    }

    /// <summary>The new-version editor starts from the shown version (not while it makes one).</summary>
    private async Task EditorAsync(VideoRowViewModel? version)
    {
        if (Editor.Busy)
        {
            return;
        }

        try
        {
            await Editor.LoadAsync(version?.Video, session.Profile).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "The new-version editor could not be loaded");
        }
    }

    /// <summary>
    /// The video is there but its thumbnail file is not: it is made again (ffmpeg, in the background), saved on the record and shown in the
    /// list and the player. A failure is logged and the area stays empty; nothing is thrown.
    /// </summary>
    private async Task ThumbnailAgainAsync(VideoRowViewModel row)
    {
        if (row.Missing || row.HasThumbnailFile || !_thumbnailing.Add(row.Video.Id))
        {
            return;
        }

        try
        {
            var name = row.Video.ThumbnailName.Length > 0 ? row.Video.ThumbnailName : VideoFolders.ThumbnailFile;
            var jpeg = Path.Combine(row.Folder, name);
            logger.LogInformation("A video's thumbnail is missing; making it again");
            var made = await Task.Run(() => media.ThumbnailAsync(row.File, jpeg, CancellationToken.None)).ConfigureAwait(true);
            if (!made)
            {
                // The reason (ffmpeg's error, or no frame that is not black) is logged by the media tools just before.
                logger.LogWarning("The thumbnail could not be made again");
                return;
            }

            if (row.Video.ThumbnailName != name)
            {
                await videos.SetThumbnailAsync(row.Video.Id, name, CancellationToken.None).ConfigureAwait(true);
                row.Video.ThumbnailName = name;
            }

            row.RefreshThumbnail();
            if (ReferenceEquals(SelectedVersion, row))
            {
                Player.Poster = row.Thumbnail;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // ffmpeg missing (ToolInstallException), a file or database error: logged with its cause, the page goes on.
            logger.LogError(ex, "The thumbnail could not be made again");
        }
        finally
        {
            _thumbnailing.Remove(row.Video.Id);
        }
    }

    private async Task DetailsAsync(VideoRowViewModel row)
    {
        try
        {
            var costs = await videos.CostsAsync(row.Video.Id, CancellationToken.None).ConfigureAwait(true);
            var v = row.Video;
            var details = new StringBuilder();
            details.Append(text.Format("produced.detail.engine", v.Engine, v.CodeModel)).Append('\n')
                .Append(text.Format("produced.detail.size", v.Width, v.Height, v.Fps, (v.SizeBytes / 1_000_000.0).ToString("0.0", CultureInfo.InvariantCulture))).Append('\n')
                .Append(text.Format("produced.detail.speech", v.SpeechModel.Length > 0 ? v.SpeechModel : text["produced.detail.none"])).Append('\n')
                .Append(text.Format("produced.detail.images", v.ImageModel.Length == 0 ? text["produced.detail.images_none"]
                    : v.ImageModel == Core.Images.ImageSources.Samples ? text["step.images.samples"] : v.ImageModel)).Append('\n')
                .Append(text.Format("produced.detail.elapsed", (v.ElapsedMs / 1000.0).ToString("0", CultureInfo.InvariantCulture))).Append('\n');
            // In the order of the work: concept share (with its run's numbers), the concept's AI edits (0.1.25; older videos have no such
            // line), narration text, image prompts, images, narration audio, video code, total. The share is stored with the video; a missing one is worked out from the concept's run, or said unknown.
            var concept = v.ConceptId is { } conceptId ? await concepts.GetAsync(conceptId, CancellationToken.None).ConfigureAwait(true) : null;
            var run = concept?.RunId is { } runId ? await runs.GetAsync(runId, CancellationToken.None).ConfigureAwait(true) : null;
            var share = VideoCostBreakdown.Share(costs, concept, run, row.Version is null or 1);
            details.Append(ShareText(share)).Append('\n');
            foreach (var line in VideoCostBreakdown.Lines(costs).Where(l => l.Step != Core.Pipeline.PipelineSteps.Concepts))
            {
                var label = text["produced.cost." + line.Step] + (line.Count > 1 ? " ×" + line.Count.ToString(CultureInfo.InvariantCulture) : string.Empty);
                var kind = line.Step == VideoCostBreakdown.NarrationText ? text["produced.cost.free"] : line.Exact ? text["produced.exact"] : text["produced.estimated"];
                details.Append(text.Format("produced.detail.cost", label, text.Usd(line.Usd), kind)).Append('\n');
            }

            // The video's own total excludes the concept share and the concept's AI edits (those runs are counted once, in the month's
            // spending); the total line adds them.
            details.Append(TotalText(v.TotalUsd, share, VideoCostBreakdown.EditsUsd(costs))).Append('\n');
            foreach (var warning in v.Warnings.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                details.Append("• ").Append(text["produced.note." + warning]).Append('\n');
            }

            details.Append(text.Format("produced.detail.folder", row.Folder));
            var prompt = concept is null
                ? text["produced.prompt.gone"]
                : text.Format("produced.prompt", concept.Title, concept.Summary, v.PromptVersion.Length > 0 ? v.PromptVersion : "1", concept.Content);
            var scenes = ScenesOf(row);
            if (ReferenceEquals(SelectedVersion, row))
            {
                Details = details.ToString();
                ConceptText = prompt;
                Scenes.Clear();
                foreach (var scene in scenes)
                {
                    Scenes.Add(scene);
                }

                ScenesNote = scenes.Count > 0 ? string.Empty : text["produced.scenes.none"];
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Video details could not be read");
            Status = text.Format("error.generic", ex.Message);
        }
    }

    /// <summary>The total line of the cost details: the video's own cost, with the concept share and the concept's AI edits when they are known.</summary>
    private string TotalText(decimal videoUsd, ConceptShareLine share, decimal editsUsd)
    {
        if (share.Kind == ConceptShareKind.Unknown)
        {
            return editsUsd > 0
                ? text.Format("produced.detail.total_unknown_edits", text.Usd(videoUsd + editsUsd), text.Usd(editsUsd))
                : text.Format("produced.detail.total_unknown", text.Usd(videoUsd));
        }

        var shareUsd = share.Known ? share.Usd : 0;
        if (editsUsd > 0)
        {
            return text.Format("produced.detail.total_edits", text.Usd(videoUsd + shareUsd + editsUsd), text.Usd(shareUsd), text.Usd(editsUsd),
                text.Usd(videoUsd));
        }

        return shareUsd > 0
            ? text.Format("produced.detail.total_share", text.Usd(videoUsd + shareUsd), text.Usd(shareUsd), text.Usd(videoUsd))
            : text.Format("produced.detail.total", text.Usd(videoUsd));
    }

    /// <summary>The concept share line of the cost details, with the run's numbers when the run is known.</summary>
    private string ShareText(ConceptShareLine share)
    {
        var amount = share.Kind switch
        {
            ConceptShareKind.Handwritten => text["produced.share.handwritten"],
            ConceptShareKind.FirstVersion => text["produced.share.first_version"],
            ConceptShareKind.Unknown => text["produced.share.unknown"],
            _ => text.Format("produced.share.amount", text.Usd(share.Usd), share.Usd == 0 ? text["produced.cost.free"]
                : share.Exact ? text["produced.exact"] : text["produced.estimated"]),
        };
        var math = share is { RunUsd: { } runUsd, Kept: > 0 } && share.Kind != ConceptShareKind.Handwritten
            ? " — " + text.Format("produced.share.math", share.Kept.Value, text.Usd(runUsd), text.Usd(VideoCostBreakdown.ConceptShare(runUsd, share.Kept.Value)))
            : string.Empty;
        return text["produced.cost.concepts"] + ": " + amount + math;
    }

    /// <summary>
    /// The scenes of a video as it was rendered (<c>source/input.json</c> of its version folder: what is on screen, the whole narration and the
    /// length of each scene). Nothing when the file is missing or unreadable (the reason is logged).
    /// </summary>
    private List<SceneText> ScenesOf(VideoRowViewModel row)
    {
        var file = Path.Combine(row.Folder, VideoFolders.Source, VideoFolders.InputFile);
        if (!File.Exists(file))
        {
            logger.LogInformation("A video has no kept render input; its narration is not shown scene by scene");
            return [];
        }

        try
        {
            var input = Core.Render.RenderInputBuilder.FromJson(File.ReadAllText(file));
            if (input is null)
            {
                logger.LogWarning("The kept render input of a video could not be read");
                return [];
            }

            return input.Scenes.Select(s => new SceneText(s.Number, text.Format("produced.scene", s.Number,
                s.DurationSeconds.ToString("0.0", text.Culture)), s.Visual, s.Caption)).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "The kept render input of a video could not be read");
            return [];
        }
    }

    /// <summary>
    /// Reloads the list; the selected video and version stay selected (a video finishing in the background reloads this page), a new
    /// version just made is selected.
    /// </summary>
    private async Task ReloadAsync()
    {
        var selected = _select ?? SelectedVersion?.Video.Id ?? Selected?.Video.Id;
        _select = null;
        var profile = session.Profile;
        var list = profile is null ? [] : await rows.ListAsync(profile.Id, _all, CancellationToken.None).ConfigureAwait(true);
        if (session.Profile?.Id != profile?.Id)
        {
            return;
        }

        _all.Clear();
        _all.AddRange(list);

        var count = _all.Select(r => r.GroupKey).Distinct(StringComparer.Ordinal).Count();
        Badge = count == 0 ? string.Empty : count.ToString(CultureInfo.InvariantCulture);
        Filter();
        var version = _all.FirstOrDefault(r => r.Video.Id == selected);
        var again = version is null ? null : Items.FirstOrDefault(r => r.GroupKey == version.GroupKey);
        if (!ReferenceEquals(again, Selected))
        {
            Selected = again;
        }
        else if (again is not null)
        {
            // The same video: its versions may have changed (one added or deleted).
            OnSelectedChanged(again);
        }

        if (version is not null && Versions.Contains(version) && !ReferenceEquals(SelectedVersion, version))
        {
            SelectedVersion = version;
        }
    }

    /// <summary>The newest version of each video (the store lists newest first), filtered by the search.</summary>
    private void Filter()
    {
        var search = TurkishFold.Fold(Search);
        CollectionSync.Apply(Items, Videos.VideoRows.Latest(_all)
            .Where(r => search.Length == 0 || TurkishFold.Fold(r.Title).Contains(search, StringComparison.Ordinal)).ToList());
    }
}
