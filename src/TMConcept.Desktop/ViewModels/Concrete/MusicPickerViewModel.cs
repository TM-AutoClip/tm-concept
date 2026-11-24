using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TMConcept.Core.Activity;
using TMConcept.Core.Music;
using TMConcept.Desktop.Localization;

namespace TMConcept.Desktop.ViewModels;

/// <summary>
/// The library music of a template (0.1.45, C4 + S6), part of the template editor ("Kütüphaneden seç"): the ready library's install (one
/// confirmation; nothing is downloaded before it), the track — chosen by each video's concept, one of the library, or one picked from an
/// Openverse search — and the search itself (only CC0, CC BY and CC BY-SA; paged; Cancel). Searching and downloading never hold the window;
/// a failure is shown here and in the activity panel. A track that asks for credit shows the note (H2): the video page offers the text after
/// the video.
/// </summary>
public sealed partial class MusicPickerViewModel : ObservableObject, IDisposable
{
    private readonly MusicPickerServices _services;
    private readonly Localizer _text;
    private CancellationTokenSource? _work;

    // The words of the shown results (the next and previous pages use them, not the box's text).
    private string _searched = string.Empty;

    /// <summary>Fills the list from the library and <paramref name="track"/> (the template's pick; null = chosen by the concept).</summary>
    public MusicPickerViewModel(MusicPickerServices services, Localizer text, MusicTrack? track)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(text);
        _services = services;
        _text = text;
        Tracks.Add(new MusicTrackOption(null, text["styles.music.auto"], text["styles.music.auto.detail"]));
        foreach (var item in services.Library.Tracks)
        {
            Tracks.Add(Option(item));
        }

        if (track is not null && Tracks.All(o => o.Track?.Id != track.Id))
        {
            Tracks.Add(Option(track));
        }

        SelectedTrack = Tracks.FirstOrDefault(o => o.Track?.Id == track?.Id) ?? Tracks[0];
        Ready = services.Library.Ready;
        var bytes = services.Library.Tracks.Sum(t => t.Bytes);
        InstallText = text.Format("styles.music.library.missing", services.Library.Tracks.Count,
            Math.Round(bytes / 1048576.0).ToString("0", CultureInfo.InvariantCulture));
    }

    /// <summary>Every track of the library is on this computer.</summary>
    [ObservableProperty]
    public partial bool Ready { get; set; }

    /// <summary>"The library is not installed: 19 tracks, about 114 MB".</summary>
    public string InstallText { get; }

    /// <summary>The tracks to choose from: chosen by the concept, the library's, the picked one.</summary>
    public ObservableCollection<MusicTrackOption> Tracks { get; } = [];

    /// <summary>The chosen track.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AttributionNote), nameof(HasAttributionNote))]
    public partial MusicTrackOption SelectedTrack { get; set; }

    /// <summary>The template's track (null = chosen by each video's concept).</summary>
    public MusicTrack? Track => SelectedTrack?.Track;

    /// <summary>The credit note (H2): the chosen track asks for credit, or the concept chooses from the CC BY library.</summary>
    public string AttributionNote => SelectedTrack?.Track switch
    {
        null => _text["styles.music.credit.library"],
        { } t when MusicLicenses.AsksCredit(t.License) => _text.Format("styles.music.credit.track", t.License),
        _ => string.Empty,
    };

    /// <summary>A credit note is shown.</summary>
    public bool HasAttributionNote => AttributionNote.Length > 0;

    /// <summary>The search words.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SearchCommand))]
    public partial string SearchQuery { get; set; } = string.Empty;

    /// <summary>A search or a download is running (Cancel is shown).</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SearchCommand), nameof(NextPageCommand), nameof(PreviousPageCommand), nameof(PickCommand), nameof(InstallCommand))]
    public partial bool Busy { get; set; }

    /// <summary>What the search did last ("12 tracks, page 1 / 4", an error…).</summary>
    [ObservableProperty]
    public partial string SearchStatus { get; set; } = string.Empty;

    /// <summary>The results of the shown page.</summary>
    public ObservableCollection<MusicTrackOption> Results { get; } = [];

    /// <summary>The shown page (1-based; 0 before a search).</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(NextPageCommand), nameof(PreviousPageCommand))]
    public partial int Page { get; set; }

    /// <summary>How many pages the search has.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(NextPageCommand))]
    public partial int PageCount { get; set; }

    /// <summary>Installs the ready library (one confirmation of the app's install flow).</summary>
    [RelayCommand(CanExecute = nameof(Idle))]
    public async Task InstallAsync()
    {
        Busy = true;
        try
        {
            Ready = await _services.InstallAsync().ConfigureAwait(true) && _services.Library.Ready;
        }
        finally
        {
            Busy = false;
        }
    }

    /// <summary>Searches Openverse for <see cref="SearchQuery"/> (page 1).</summary>
    [RelayCommand(CanExecute = nameof(CanSearch))]
    public Task SearchAsync()
    {
        _searched = SearchQuery.Trim();
        return LoadAsync(1);
    }

    /// <summary>The next page.</summary>
    [RelayCommand(CanExecute = nameof(CanNext))]
    public Task NextPageAsync() => LoadAsync(Page + 1);

    /// <summary>The previous page.</summary>
    [RelayCommand(CanExecute = nameof(CanPrevious))]
    public Task PreviousPageAsync() => LoadAsync(Page - 1);

    /// <summary>Stops the running search or download.</summary>
    [RelayCommand]
    public void Cancel() => _work?.Cancel();

    /// <summary>Downloads the picked result into the app's music folder and makes it the template's track.</summary>
    [RelayCommand(CanExecute = nameof(Idle))]
    public async Task PickAsync(MusicTrackOption? option)
    {
        if (option?.Track is not { } track)
        {
            return;
        }

        using var work = Start();
        try
        {
            SearchStatus = _text.Format("styles.music.search.downloading", track.Title);
            var file = await _services.Search.DownloadAsync(track, _services.Library.PickedFolder, work.Token).ConfigureAwait(true);
            var picked = new MusicTrack
            {
                Id = track.Id, Title = track.Title, Artist = track.Artist, PageUrl = track.PageUrl, DownloadUrl = track.DownloadUrl, License = track.License,
                Attribution = track.Attribution, DurationSeconds = track.DurationSeconds, Bytes = new FileInfo(file).Length, File = file,
            };
            foreach (var old in Tracks.Where(o => o.Track?.Id.StartsWith(MusicLibraryRules.OpenversePrefix, StringComparison.Ordinal) == true).ToList())
            {
                Tracks.Remove(old);
            }

            var added = Option(picked);
            Tracks.Add(added);
            SelectedTrack = added;
            SearchStatus = _text.Format("styles.music.search.picked", track.Title);
        }
        catch (OperationCanceledException) when (work.IsCancellationRequested)
        {
            SearchStatus = _text["styles.music.search.cancelled"];
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException or InvalidOperationException
            or OperationCanceledException)
        {
            Fail("styles.music.search.download_failed", ex.Message);
        }
        finally
        {
            Finish(work);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _work?.Cancel();
        _work?.Dispose();
        _work = null;
    }

    private bool Idle() => !Busy;

    private bool CanSearch() => !Busy && SearchQuery.Trim().Length > 0;

    private bool CanNext() => !Busy && Page > 0 && Page < PageCount;

    private bool CanPrevious() => !Busy && Page > 1;

    private async Task LoadAsync(int page)
    {
        if (_searched.Length == 0)
        {
            return;
        }

        using var work = Start();
        try
        {
            SearchStatus = _text["styles.music.search.running"];
            var result = await _services.Search.SearchAsync(_searched, page, work.Token).ConfigureAwait(true);
            Results.Clear();
            foreach (var track in result.Tracks)
            {
                Results.Add(new MusicTrackOption(track, Label(track), Detail(track)));
            }

            Page = result.Page;
            PageCount = result.PageCount;
            SearchStatus = result.Tracks.Count == 0
                ? _text["styles.music.search.none"]
                : _text.Format("styles.music.search.found", result.Tracks.Count, result.Page, result.PageCount, result.Dropped);
        }
        catch (OperationCanceledException) when (work.IsCancellationRequested)
        {
            SearchStatus = _text["styles.music.search.cancelled"];
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidDataException or IOException or OperationCanceledException)
        {
            Fail("styles.music.search.failed", ex.Message);
        }
        finally
        {
            Finish(work);
        }
    }

    private CancellationTokenSource Start()
    {
        _work?.Dispose();
        _work = new CancellationTokenSource();
        Busy = true;
        return _work;
    }

    private void Finish(CancellationTokenSource work)
    {
        if (ReferenceEquals(_work, work))
        {
            _work = null;
        }

        Busy = false;
    }

    private void Fail(string key, string detail)
    {
        SearchStatus = _text.Format(key, detail);
        _services.Activity.Add(ActivityLevel.Error, SearchStatus);
    }

    private MusicTrackOption Option(MusicTrack track) => new(track, Label(track),
        track.Moods.Count > 0 ? string.Join(", ", track.Moods.Concat(track.Styles).Select(t => _text["music.tag." + t])) + " · " + Length(track) : Detail(track));

    private static string Label(MusicTrack track) => track.Artist.Length > 0 ? track.Title + " — " + track.Artist : track.Title;

    private static string Detail(MusicTrack track) => track.License + " · " + Length(track);

    private static string Length(MusicTrack track) =>
        track.DurationSeconds > 0 ? TimeSpan.FromSeconds(track.DurationSeconds).ToString(@"m\:ss", CultureInfo.InvariantCulture) : "–";
}
