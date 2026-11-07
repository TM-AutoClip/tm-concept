using Avalonia.Headless.XUnit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using TMConcept.Core.Media;
using TMConcept.Core.Paths;
using TMConcept.Core.Profiles;
using TMConcept.Core.Tools;
using TMConcept.Core.Videos;
using TMConcept.Desktop.Startup;
using TMConcept.Desktop.ViewModels;
using TMConcept.Engines.Playback;
using TMConcept.Engines.Tools;

namespace TMConcept.Tests.Desktop;

/// <summary>
/// The player's seek bar and poster. The ffmpeg found here is an empty placeholder file: no picture is decoded (that is logged), only the
/// view-model logic and the engine's position are checked.
/// </summary>
public sealed class PlayerTests
{
    [AvaloniaFact]
    public async Task DraggingTheSeekBarHoldsThePositionUpdatesAndSeeksOnceOnRelease()
    {
        using var h = new DesktopHarness(PlaceholderFfmpeg);
        var video = Path.Combine(h.Paths.DataDirectory, "video.mp4");
        File.WriteAllBytes(video, new byte[16]);
        var player = h.Get<PlayerViewModel>();
        var playback = h.Get<FfmpegPlayback>();
        player.Load(video, string.Empty, 1080, 1920, 30, 10_000);
        player.HasVideo.ShouldBeTrue(player.Message);

        // Dragging: the thumb stays under the hand although the 100 ms update runs, and nothing seeks yet.
        player.BeginSeek();
        player.PositionMs = 6_000;
        player.Tick();
        player.PositionMs.ShouldBe(6_000);
        await Task.Delay(PlayerViewModel.SeekDelay * 3);
        playback.PositionMs.ShouldBe(0);

        // Release: one seek to where the thumb is; the update then agrees with it.
        player.EndSeek();
        playback.PositionMs.ShouldBe(6_000);
        player.Tick();
        player.PositionMs.ShouldBe(6_000);
        player.EndSeek();
        playback.PositionMs.ShouldBe(6_000);

        // A press on the track (the slider moves the thumb there) seeks on release too.
        player.BeginSeek();
        player.PositionMs = 2_500;
        player.EndSeek();
        playback.PositionMs.ShouldBe(2_500);

        // A change without the pointer (the keyboard) seeks once the value rests.
        player.PositionMs = 4_000;
        for (var i = 0; i < 100 && playback.PositionMs != 4_000; i++)
        {
            await Task.Delay(20);
        }

        playback.PositionMs.ShouldBe(4_000);

        // A press and release without moving the thumb (a plain click on it) does not seek.
        player.BeginSeek();
        player.EndSeek();
        playback.PositionMs.ShouldBe(4_000);

        // Closing drops a seek that was still waiting: nothing seeks afterwards.
        player.PositionMs = 1_000;
        player.Close();
        await Task.Delay(PlayerViewModel.SeekDelay * 3);
        playback.PositionMs.ShouldBe(4_000);
        playback.HasFrame.ShouldBeFalse();
        player.Frame.ShouldBeNull();
        player.HasVideo.ShouldBeTrue();
    }

    [AvaloniaFact]
    public async Task AMissingThumbnailIsMadeAgainAndShownAndAFailureIsOnlyLogged()
    {
        var media = new FakeMedia();
        using var h = new DesktopHarness(s => s.AddSingleton<IMediaTools>(media));
        await FirstRunTests.CreateAsync(h);
        var profile = new Profile { Name = "Byzantium", ContentLanguage = ContentLanguages.English, ConceptText = "x" };
        await h.Get<IProfileStore>().SaveAsync(profile, CancellationToken.None);
        await h.Get<AppSession>().SelectProfileAsync(profile, CancellationToken.None);
        var library = h.Get<IAppPaths>().LibraryDirectory;
        var records = new List<VideoRecord>();
        foreach (var name in new[] { "no-thumbnail", "thumbnail-deleted" })
        {
            var folder = Path.Combine(library, "Byzantium", name);
            Directory.CreateDirectory(folder);
            File.WriteAllBytes(Path.Combine(folder, "video.mp4"), new byte[16]);
            var record = new VideoRecord
            {
                ProfileId = profile.Id,
                Title = name,
                Folder = LibraryPaths.Relative(library, folder),
                FileName = "video.mp4",
                ThumbnailName = name == "no-thumbnail" ? string.Empty : "cover.jpg",
            };
            await h.Get<IVideoStore>().AddAsync(record, [], CancellationToken.None);
            records.Add(record);
        }

        var page = h.Get<ProducedViewModel>();
        await page.ActivateAsync();
        foreach (var record in records)
        {
            var row = page.Items.Single(r => r.Video.Id == record.Id);
            row.Thumbnail.ShouldBeEmpty();
            page.Selected = row;
            await page.ThumbnailWork;

            var expected = Path.Combine(row.Folder, record.ThumbnailName.Length > 0 ? record.ThumbnailName : VideoFolders.ThumbnailFile);
            row.Thumbnail.ShouldBe(expected);
            page.Player.Poster.ShouldBe(expected);
            (await h.Get<IVideoStore>().GetAsync(record.Id, CancellationToken.None))!.ThumbnailName.ShouldBe(Path.GetFileName(expected));
        }

        // ffmpeg missing: nothing is thrown, the poster stays empty.
        media.Fail = true;
        File.Delete(page.Items[0].Thumbnail);
        page.Selected = null;
        page.Selected = page.Items[0];
        await page.ThumbnailWork;
        page.Player.Poster.ShouldBeEmpty();
        media.Calls.ShouldBe(3);
    }

    /// <summary>An empty ffmpeg (and ffprobe) on a private search path, used as the "system copy" (no real decoding happens).</summary>
    private static void PlaceholderFfmpeg(IServiceCollection services)
    {
        services.AddSingleton(s =>
        {
            // Inside the harness's data folder, so it is removed with it.
            var paths = s.GetRequiredService<IAppPaths>();
            var bin = Path.Combine(paths.DataDirectory, "placeholder-bin");
            Directory.CreateDirectory(bin);
            var suffix = OperatingSystem.IsWindows() ? ".exe" : string.Empty;
            File.WriteAllBytes(Path.Combine(bin, "ffmpeg" + suffix), []);
            File.WriteAllBytes(Path.Combine(bin, "ffprobe" + suffix), []);
            Directory.CreateDirectory(Path.Combine(paths.ToolsDirectory, "ffmpeg"));
            File.WriteAllText(Path.Combine(paths.ToolsDirectory, "ffmpeg", "use-system"), "test");
            return new ToolInstaller(paths, s.GetRequiredService<IToolPlatform>(), s.GetRequiredService<ToolDownloader>(), NullLogger<ToolInstaller>.Instance)
            {
                SearchPath = bin,
            };
        });
    }

    private sealed class FakeMedia : IMediaTools
    {
        public IReadOnlyList<string> RequiredComponents { get; } = [];

        public bool Fail { get; set; }

        public int Calls { get; private set; }

        public Task MuxAsync(string folder, string silentVideo, IReadOnlyList<(string Wav, long StartMs)> clips, string? assFile, string output,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public async Task<bool> ThumbnailAsync(string video, string jpeg, CancellationToken cancellationToken)
        {
            Calls++;
            if (Fail)
            {
                throw new ToolInstallException("ffmpeg is not installed.") { Code = "missing" };
            }

            await File.WriteAllBytesAsync(jpeg, [0xFF, 0xD8, 0xFF], cancellationToken);
            return true;
        }
    }
}
