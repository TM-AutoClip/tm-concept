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
using TMConcept.Desktop.Os;
using TMConcept.Desktop.Startup;

namespace TMConcept.Desktop.ViewModels;

/// <summary>
/// 🎞 Produced videos: one row per video (its newest version): thumbnail (the first frame that is not black), title, date, length, cost and
/// warning badges (a file deleted from outside shows "file missing"). The details show the versions of the video (v1, v2…; choose one to
/// watch it, delete one); the in-app player (a chosen video does not start by itself) with "open in the system player" beside it; the
/// whole narration scene by scene, the whole concept text, settings and cost per step; "New version" (a new voice for an edited narration
/// rendered here with the same code, or new code for another engine: <see cref="VersionEditorViewModel"/>); open the folder; produce
/// again (the concept goes back to the approved list of the video page; the new video is a new row); delete the version (the files can be
/// moved to the recycle bin in the same step). A chosen version whose thumbnail file is missing gets it made again in the background (the
/// same rule: the first frame that is not black), so the player and the list do not show a black area.
/// </summary>
public sealed partial class ProducedViewModel(AppSession session, IVideoStore videos, IConceptStore concepts, Videos.VideoRows rows, Videos.VideoFiles files,
    IRecycleBin recycleBin, IConfirmDialog confirm, ConceptEvents events, Concepts.ConceptActions actions, IActivityLog activity, PlayerViewModel player,
    IMediaTools media, Localizer text, VersionEditorViewModel editor, ILogger<ProducedViewModel> logger)
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
        if (!_attached)
        {
            _attached = true;
            Editor.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(VersionEditorViewModel.Busy))
                {
                    Working = Editor.Busy;
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

    /// <summary>Deletes the shown version's record (and, if ticked, moves its files to the recycle bin); the other versions stay.</summary>
    [RelayCommand]
    public async Task DeleteAsync()
    {
        if ((SelectedVersion ?? Selected) is not { } row)
        {
            return;
        }

        var answer = await confirm.AskAsync(text["produced.delete.title"], text.Format("produced.delete.message", row.Title), text["common.delete"], danger: true,
            checkbox: text["produced.delete.files"]).ConfigureAwait(true);
        if (!answer.Confirmed)
        {
            return;
        }

        if (answer.Checked && Directory.Exists(row.Folder))
        {
            try
            {
                // A version folder (v1/, v2/…) that is alone in its concept's folder goes with that folder, so the recycle bin shows the title.
                var parent = Path.GetDirectoryName(row.Folder);
                var alone = parent is not null && row.Version is not null && Directory.EnumerateFileSystemEntries(parent).Count() == 1;
                await recycleBin.MoveAsync(alone ? parent! : row.Folder, CancellationToken.None).ConfigureAwait(true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or OperationCanceledException)
            {
                logger.LogError(ex, "The video folder could not be moved to the recycle bin");
                activity.Add(ActivityLevel.Error, text.Format("produced.recycle_failed", ex.Message));
                Status = text.Format("produced.recycle_failed", ex.Message);
                return;
            }
        }

        if (ReferenceEquals(SelectedVersion, row) || ReferenceEquals(Selected, row))
        {
            // The player holds the file open on Windows: it is released before the folder moves.
            Player.Unload();
        }

        await videos.DeleteAsync(row.Video.Id, CancellationToken.None).ConfigureAwait(true);

        // Another version of the same video stays shown (the newest that is left).
        _select = Versions.LastOrDefault(v => !ReferenceEquals(v, row))?.Video.Id;
        var deleted = answer.Checked ? "produced.deleted_files" : "produced.deleted";
        activity.Add(ActivityLevel.Info, text.Format(deleted, row.Title), text.Format(deleted, ActivityPlaceholders.Title));
        await ReloadAsync().ConfigureAwait(true);
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
            return;
        }

        Player.Load(value.File, value.Thumbnail, value.Video.Width, value.Video.Height, value.Video.Fps, value.Video.DurationMs);
        ThumbnailWork = ThumbnailAgainAsync(value);
        _ = DetailsAsync(value);
        _ = EditorAsync(value);
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
            // In the order of the work: concept share, narration text, image prompts, images, narration audio, video code, total.
            decimal share = 0;
            foreach (var line in VideoCostBreakdown.Lines(costs))
            {
                var label = text["produced.cost." + line.Step] + (line.Count > 1 ? " ×" + line.Count.ToString(CultureInfo.InvariantCulture) : string.Empty);
                var kind = line.Step == VideoCostBreakdown.NarrationText ? text["produced.cost.free"] : line.Exact ? text["produced.exact"] : text["produced.estimated"];
                details.Append(text.Format("produced.detail.cost", label, text.Usd(line.Usd), kind)).Append('\n');
                share += line.Step == Core.Pipeline.PipelineSteps.Concepts ? line.Usd : 0;
            }

            // The video's own total excludes the concept share (that run is counted once, with the concept runs).
            details.Append(share > 0
                ? text.Format("produced.detail.total_share", text.Usd(v.TotalUsd + share), text.Usd(share), text.Usd(v.TotalUsd))
                : text.Format("produced.detail.total", text.Usd(v.TotalUsd))).Append('\n');
            foreach (var warning in v.Warnings.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                details.Append("• ").Append(text["produced.note." + warning]).Append('\n');
            }

            details.Append(text.Format("produced.detail.folder", row.Folder));
            var concept = v.ConceptId is { } conceptId ? await concepts.GetAsync(conceptId, CancellationToken.None).ConfigureAwait(true) : null;
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
