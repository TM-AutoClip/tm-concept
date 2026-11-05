using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using TMConcept.Core.Activity;
using TMConcept.Core.Paths;
using TMConcept.Core.Text;
using TMConcept.Core.Videos;
using TMConcept.Desktop.Dialogs;
using TMConcept.Desktop.Localization;
using TMConcept.Desktop.Os;
using TMConcept.Desktop.Startup;

namespace TMConcept.Desktop.ViewModels;

/// <summary>
/// 🎞 Produced videos: thumbnail (the first frame that is not black), title, date, length, cost and warning badges; details (settings, cost
/// per step, the AI voice / non-commercial notes, files); play with the system player, open the folder; delete the record (the files can be
/// moved to the recycle bin in the same step).
/// </summary>
public sealed partial class ProducedViewModel(AppSession session, IVideoStore videos, IAppPaths paths, ShellLauncher shell, IRecycleBin recycleBin,
    IConfirmDialog confirm, ConceptEvents events, IActivityLog activity, Localizer text, ILogger<ProducedViewModel> logger) : PageViewModelBase
{
    private readonly List<VideoRowViewModel> _all = [];
    private bool _attached;

    /// <inheritdoc />
    public override string TitleKey => "nav.produced";

    /// <inheritdoc />
    public override string Glyph => "🎞";

    /// <inheritdoc />
    public override int Order => 30;

    /// <summary>Videos (filtered).</summary>
    public ObservableCollection<VideoRowViewModel> Items { get; } = [];

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

    /// <inheritdoc />
    public override Task ActivateAsync()
    {
        if (!_attached)
        {
            _attached = true;
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

    /// <summary>Plays the selected video with the system player.</summary>
    [RelayCommand]
    public void Play()
    {
        if (Selected is { Missing: false } row)
        {
            Open(row.File);
        }
    }

    /// <summary>Opens the video folder.</summary>
    [RelayCommand]
    public void OpenFolder()
    {
        if (Selected is { } row && Directory.Exists(row.Folder))
        {
            Open(row.Folder);
        }
    }

    /// <summary>Deletes the record (and, if ticked, moves the files to the recycle bin).</summary>
    [RelayCommand]
    public async Task DeleteAsync()
    {
        if (Selected is not { } row)
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
                await recycleBin.MoveAsync(row.Folder, CancellationToken.None).ConfigureAwait(true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or OperationCanceledException)
            {
                logger.LogError(ex, "The video folder could not be moved to the recycle bin");
                activity.Add(ActivityLevel.Error, text.Format("produced.recycle_failed", ex.Message));
                return;
            }
        }

        await videos.DeleteAsync(row.Video.Id, CancellationToken.None).ConfigureAwait(true);
        activity.Add(ActivityLevel.Info, text.Format(answer.Checked ? "produced.deleted_files" : "produced.deleted", row.Title));
        await ReloadAsync().ConfigureAwait(true);
    }

    partial void OnSearchChanged(string value) => Filter();

    partial void OnSelectedChanged(VideoRowViewModel? value)
    {
        Details = string.Empty;
        if (value is not null)
        {
            _ = DetailsAsync(value);
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
                .Append(text.Format("produced.detail.images", v.ImageModel)).Append('\n')
                .Append(text.Format("produced.detail.elapsed", (v.ElapsedMs / 1000.0).ToString("0", CultureInfo.InvariantCulture))).Append('\n');
            foreach (var cost in costs)
            {
                details.Append(text.Format("produced.detail.cost", text["step." + cost.Step], text.Usd(cost.Usd), cost.Exact ? text["produced.exact"] : text["produced.estimated"]))
                    .Append('\n');
            }

            details.Append(text.Format("produced.detail.total", text.Usd(v.TotalUsd))).Append('\n');
            foreach (var warning in v.Warnings.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                details.Append("• ").Append(text["produced.note." + warning]).Append('\n');
            }

            details.Append(text.Format("produced.detail.folder", row.Folder));
            if (ReferenceEquals(Selected, row))
            {
                Details = details.ToString();
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Video details could not be read");
            Status = text.Format("error.generic", ex.Message);
        }
    }

    private void Open(string path)
    {
        try
        {
            shell.OpenPath(path);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            logger.LogWarning(ex, "Could not open the file");
            Status = text.Format("error.generic", ex.Message);
        }
    }

    private async Task ReloadAsync()
    {
        _all.Clear();
        Selected = null;
        if (session.Profile is { } profile)
        {
            foreach (var video in await videos.ListAsync(profile.Id, CancellationToken.None).ConfigureAwait(true))
            {
                string folder;
                try
                {
                    folder = LibraryPaths.Resolve(paths.LibraryDirectory, video.Folder);
                }
                catch (InvalidOperationException ex)
                {
                    logger.LogWarning(ex, "A video folder is outside the library and is ignored");
                    continue;
                }

                _all.Add(new VideoRowViewModel(video, folder, text));
            }
        }

        Badge = _all.Count == 0 ? string.Empty : _all.Count.ToString(CultureInfo.InvariantCulture);
        Filter();
    }

    private void Filter()
    {
        var search = TurkishFold.Fold(Search);
        Items.Clear();
        foreach (var row in _all.Where(r => search.Length == 0 || TurkishFold.Fold(r.Title).Contains(search, StringComparison.Ordinal)))
        {
            Items.Add(row);
        }
    }
}
