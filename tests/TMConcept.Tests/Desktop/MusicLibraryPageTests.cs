using Avalonia.Headless.XUnit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Shouldly;
using TMConcept.Core.Activity;
using TMConcept.Core.Music;
using TMConcept.Core.Styles;
using TMConcept.Desktop.Dialogs;
using TMConcept.Desktop.Localization;
using TMConcept.Desktop.ViewModels;

namespace TMConcept.Tests.Desktop;

/// <summary>
/// The library music of a template in Settings → Templates (0.1.45, C4 + S6): "Kütüphaneden seç" is greyed out and refused until the library
/// is installed (only after the user's yes); the Openverse search lists what the fake search found, a failed search reaches the activity
/// panel, and a picked track is downloaded (fake) into the app's music folder, becomes the template's track with its file, and shows the
/// credit note of its CC BY licence. Nothing reaches the network.
/// </summary>
public sealed class MusicLibraryPageTests
{
    private static async Task<(DesktopHarness H, VideoStyleEditorViewModel Editor)> EditorAsync(FakeSearch search)
    {
        var h = new DesktopHarness(s =>
        {
            s.AddSingleton<IConfirmDialog>(new FakeConfirm());
            s.RemoveAll<IMusicSearch>();
            s.AddSingleton<IMusicSearch>(search);
        });
        await FirstRunTests.CreateAsync(h);
        var section = (await h.Get<SettingsViewModel>().ShowAsync<VideoStylesSettingsViewModel>()).ShouldNotBeNull();
        section.Add();
        return (h, section.Editor.ShouldNotBeNull());
    }

    [AvaloniaFact]
    public async Task TheLibraryCannotBeChosenUntilItIsInstalled()
    {
        var (h, editor) = await EditorAsync(new FakeSearch());
        using var _ = h;
        var text = h.Get<Localizer>();

        var library = editor.MusicSources.Single(c => c.Value == (int)MusicSource.Library);
        library.Label.ShouldBe(text["style.music.library"]);
        library.Enabled.ShouldBeFalse("the library is not on this computer");
        editor.MusicSources.Where(c => c.Value is not ((int)MusicSource.Library or (int)MusicSource.GenerateLocal)).ShouldAllBe(c => c.Enabled,
            "0.1.46: the local music model is greyed out until installed too");
        editor.MusicPicker.ShouldNotBeNull().Ready.ShouldBeFalse();
        editor.MusicPicker!.InstallText.ShouldContain(MusicCatalog.Shipped.Tracks.Count.ToString(text.Culture));

        editor.MusicSource = library;

        editor.MusicSource.Value.ShouldBe((int)MusicSource.Off, "refused");
        editor.MusicLibrary.ShouldBeFalse();
        editor.MusicNote.ShouldBe(text["styles.music.library.not_ready"]);
        editor.ToStyle().Music.Source.ShouldBe(MusicSource.Off);
    }

    [AvaloniaFact]
    public async Task ATrackFoundOnOpenverseIsDownloadedAndBecomesTheTemplatesMusic()
    {
        var search = new FakeSearch();
        var (h, editor) = await EditorAsync(search);
        using var _ = h;
        var text = h.Get<Localizer>();
        var picker = editor.MusicPicker.ShouldNotBeNull();
        picker.Track.ShouldBeNull("chosen by the concept until a track is picked");
        picker.AttributionNote.ShouldBe(text["styles.music.credit.library"]);

        picker.SearchQuery = "calm sea";
        await picker.SearchCommand.ExecuteAsync(null);

        search.Queries.ShouldBe([("calm sea", 1)]);
        picker.Results.Count.ShouldBe(2);
        picker.Page.ShouldBe(1);
        picker.PageCount.ShouldBe(3);
        picker.NextPageCommand.CanExecute(null).ShouldBeTrue();
        picker.SearchStatus.ShouldBe(text.Format("styles.music.search.found", 2, 1, 3, 4));

        await picker.PickCommand.ExecuteAsync(picker.Results[0]);

        var track = picker.Track.ShouldNotBeNull();
        track.Id.ShouldBe("openverse:wave-1");
        File.Exists(track.File).ShouldBeTrue();
        track.File.ShouldStartWith(h.Get<IMusicLibrary>().PickedFolder);
        picker.AttributionNote.ShouldBe(text.Format("styles.music.credit.track", "CC BY 4.0"));

        // The template keeps the pick (the library itself is not needed for it) once the source is the library.
        var style = editor.ToStyle();
        style.Music.Track.ShouldBeNull("the source is still off");
    }

    [AvaloniaFact]
    public async Task AFailedSearchIsShownAndReachesTheActivityPanel()
    {
        var search = new FakeSearch { Fail = true };
        var (h, editor) = await EditorAsync(search);
        using var _ = h;
        var picker = editor.MusicPicker.ShouldNotBeNull();

        picker.SearchQuery = "storm";
        await picker.SearchCommand.ExecuteAsync(null);

        picker.Results.ShouldBeEmpty();
        picker.Busy.ShouldBeFalse();
        picker.SearchStatus.ShouldContain("429");
        h.Get<IActivityFeed>().Recent().ShouldContain(l => l.Level == ActivityLevel.Error && l.Text.Contains("429"));
    }

    /// <summary>A search that answers from memory (no network) and "downloads" by writing a small file.</summary>
    private sealed class FakeSearch : IMusicSearch
    {
        public List<(string Query, int Page)> Queries { get; } = [];

        public bool Fail { get; init; }

        public Task<MusicSearchPage> SearchAsync(string query, int page, CancellationToken cancellationToken)
        {
            Queries.Add((query, page));
            if (Fail)
            {
                throw new HttpRequestException("Response status code does not indicate success: 429 (Too Many Requests).");
            }

            IReadOnlyList<MusicTrack> tracks =
            [
                new() { Id = "openverse:wave-1", Title = "Wave", Artist = "Ada", License = "CC BY 4.0", Attribution = "\"Wave\" by Ada", DownloadUrl = "https://x.test/w.mp3",
                    DurationSeconds = 95 },
                new() { Id = "openverse:wind-2", Title = "Wind", Artist = "Bo", License = "CC0 1.0", DownloadUrl = "https://x.test/v.mp3", DurationSeconds = 40 },
            ];
            return Task.FromResult(new MusicSearchPage(tracks, page, 3, 4));
        }

        public async Task<string> DownloadAsync(MusicTrack track, string folder, CancellationToken cancellationToken)
        {
            Directory.CreateDirectory(folder);
            var file = Path.Combine(folder, track.Id.Replace(':', '-') + ".mp3");
            await File.WriteAllBytesAsync(file, [0x49, 0x44, 0x33], cancellationToken);
            return file;
        }
    }
}
