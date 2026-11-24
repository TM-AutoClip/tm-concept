using Avalonia.Headless.XUnit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Shouldly;
using TMConcept.Core.Chat;
using TMConcept.Core.Concepts;
using TMConcept.Core.Engines;
using TMConcept.Core.Keys;
using TMConcept.Core.Media;
using TMConcept.Core.Models;
using TMConcept.Core.Paths;
using TMConcept.Core.Pipeline;
using TMConcept.Core.Profiles;
using TMConcept.Core.Speech;
using TMConcept.Core.Styles;
using TMConcept.Core.Videos;
using TMConcept.Desktop.Dialogs;
using TMConcept.Desktop.Startup;
using TMConcept.Tests.Providers;
using static TMConcept.Tests.Desktop.VideoPageTests;

namespace TMConcept.Tests.Desktop;

/// <summary>
/// Background music in a made video (0.1.44, C3 + F15), end to end with fakes (no real voice or ffmpeg): the user's own file is checked
/// before the paid code call, mixed under the narration with the ducking envelope, kept in the version folder with its asset record, and
/// used again by a new version; a damaged file stops the video; music off changes nothing.
/// </summary>
public sealed class MusicVideoTests
{
    private const string Content = "[on screen: a map]\nIn 672 the city was under siege.\n[on screen: burning ships]\nA strange fire burned on the water.";

    private static ChatResponse Code(string code) => new("```js\n" + code + "\n```", new ChatUsage(500, 0, 0, 800), null, "end_turn", "r");

    private static DesktopHarness Harness(FakeChat chat, FakeVoice voice, FakeEngine engine) => new(s =>
    {
        s.AddSingleton<IChatClientFactory>(chat);
        s.RemoveAll<IVideoEngine>();
        s.AddSingleton<IVideoEngine>(engine);
        s.AddSingleton<IMediaTools, FakeMedia>();
        s.RemoveAll<ISpeechEngine>();
        s.AddSingleton<ISpeechEngine>(voice);
        s.AddSingleton<IConfirmDialog>(new FakeConfirm());
    });

    /// <summary>A template with the user's own music file (written into the test's data folder).</summary>
    private static VideoStyle WithMusic(DesktopHarness h, string attribution = "")
    {
        var file = Path.Combine(h.Get<IAppPaths>().DataDirectory, "user-music", "My Song.MP3");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllBytes(file, [0x49, 0x44, 0x33, 0x04, 0x00]);
        return new VideoStyle { Name = "Müzikli", Music = new VideoMusic { Source = MusicSource.OwnFile, File = file, Attribution = attribution } };
    }

    private static async Task<(Profile Profile, VideoRecord Video)> MakeAsync(DesktopHarness h, VideoStyle? style)
    {
        await FirstRunTests.CreateAsync(h);
        var profile = new Profile { Name = "Byzantium", ContentLanguage = ContentLanguages.English, ConceptText = "x" };
        await h.Get<IProfileStore>().SaveAsync(profile, CancellationToken.None);
        await h.Get<AppSession>().SelectProfileAsync(profile, CancellationToken.None);
        var key = new ApiKey { Provider = "anthropic", Label = "k", Secret = "fake-test-key-video-0123" };
        await h.Get<IApiKeyStore>().SaveAsync(key, CancellationToken.None);
        await h.Get<IStepModelStore>().SaveAsync(new StepModelChoice { Step = PipelineSteps.Speech, ProfileId = profile.Id, Provider = ModelProviders.Local,
            Model = new FakeVoice().Code }, CancellationToken.None);
        var concept = new Concept { ProfileId = profile.Id, Title = "Greek fire", Summary = "s", Content = Content, Language = "en", Status = ConceptStatus.Queued };
        await h.Get<IConceptStore>().AddAsync([concept], CancellationToken.None);
        var options = new VideoOptions { Speech = true };
        if (style is not null)
        {
            VideoStyleRules.Snapshot(options, style);
        }

        var model = new ResolvedModel(h.Get<IModelCatalog>().Find("anthropic", "claude-sonnet-5-5")!, key, string.Empty);
        var stored = (await h.Get<IConceptStore>().GetAsync(concept.Id, CancellationToken.None))!;
        var video = await h.Get<VideoPipeline>().RunAsync(new VideoJob(stored, profile, options, model), null, CancellationToken.None);
        return (profile, video);
    }

    private static string Folder(DesktopHarness h, VideoRecord record) => LibraryPaths.Resolve(h.Get<IAppPaths>().LibraryDirectory, record.Folder);

    [AvaloniaFact]
    public async Task OwnMusicIsDuckedUnderTheNarrationAndKeptWithTheVersion()
    {
        var engine = new FakeEngine();
        var voice = new FakeVoice { Samples = 48_000 };
        using var h = Harness(new FakeChat().Answer(_ => Code(engine.SampleCode)), voice, engine);
        var media = (FakeMedia)h.Get<IMediaTools>();
        media.MusicMs = 600_000;

        var (_, video) = await MakeAsync(h, WithMusic(h, "Music: \"My Song\" by Me"));

        // Mixed in: the copy in the work folder, cut (a long track), the envelope ducks under the narration, a fade-out.
        var music = media.Overlays.Single().Overlay.Music.ShouldNotBeNull();
        Path.GetFileName(music.File).ShouldBe("My Song.mp3");
        music.Loop.ShouldBeFalse("a 10-minute track is only cut");
        music.DurationSeconds.ShouldBe(video.DurationMs / 1000.0, 0.01);
        music.Volume.ShouldStartWith("0.50119-");
        music.Volume.ShouldContain("min(1,min(clip((t");
        music.FadeOutSeconds.ShouldBe(MusicMixDefaults.FadeOutSeconds);
        media.MusicRead.ShouldBe(["My Song.mp3"], "ffmpeg read the copy once, before the code");

        // The version keeps the track and its asset record with the attribution the user wrote.
        var folder = Folder(h, video);
        File.Exists(Path.Combine(folder, VideoFolders.Music, "My Song.mp3")).ShouldBeTrue();
        var assets = await AssetLedger.ReadAsync(folder, CancellationToken.None);
        var entry = assets.ShouldHaveSingleItem();
        entry.Kind.ShouldBe(AssetKinds.Music);
        entry.Name.ShouldBe("My Song");
        entry.License.ShouldBe(AssetLicenses.OwnFile);
        AssetLedger.AttributionText(assets).ShouldBe("Music: \"My Song\" by Me");
    }

    [AvaloniaFact]
    public async Task AShortTrackLoopsAndANewVersionUsesTheKeptTrack()
    {
        var engine = new FakeEngine();
        var voice = new FakeVoice();
        using var h = Harness(new FakeChat().Answer(_ => Code(engine.SampleCode)), voice, engine);
        var media = (FakeMedia)h.Get<IMediaTools>();
        media.MusicMs = 500;
        var style = WithMusic(h);

        var (profile, first) = await MakeAsync(h, style);
        media.Overlays.Single().Overlay.Music.ShouldNotBeNull().Loop.ShouldBeTrue("a half-second track plays again until the video ends");
        AssetLedger.AttributionText(await AssetLedger.ReadAsync(Folder(h, first), CancellationToken.None)).ShouldBeEmpty("no attribution written");

        // The original is gone: a new voice still has the music kept with the video.
        File.Delete(style.Music.File);
        var remaker = h.Get<VideoRemaker>();
        var source = remaker.Load(first);
        var setup = await h.Get<SpeechService>().SetupAsync(new StepModelChoice { Step = PipelineSteps.Speech, Provider = ModelProviders.Local, Model = voice.Code },
            ContentLanguages.English, CancellationToken.None);
        var second = await remaker.RevoiceAsync(new RevoiceJob(source, profile, source.Texts, setup), null, CancellationToken.None);

        media.Overlays.Count.ShouldBe(2);
        media.Overlays[1].Overlay.Music.ShouldNotBeNull();
        File.Exists(Path.Combine(Folder(h, second), VideoFolders.Music, "My Song.mp3")).ShouldBeTrue();
        (await AssetLedger.ReadAsync(Folder(h, second), CancellationToken.None)).ShouldHaveSingleItem();
    }

    [AvaloniaFact]
    public async Task ADamagedMusicFileStopsTheVideoBeforeTheCodeIsPaidFor()
    {
        var engine = new FakeEngine();
        var chat = new FakeChat().Answer(_ => Code(engine.SampleCode));
        using var h = Harness(chat, new FakeVoice(), engine);
        ((FakeMedia)h.Get<IMediaTools>()).MusicMs = null;

        var failure = await Should.ThrowAsync<VideoException>(() => MakeAsync(h, WithMusic(h)));

        failure.Code.ShouldBe("music");
        chat.Requests.ShouldBeEmpty("no code was asked for");
        ((FakeMedia)h.Get<IMediaTools>()).Overlays.ShouldBeEmpty();
    }

    [AvaloniaFact]
    public async Task AMissingMusicFileStopsTheVideoToo()
    {
        var engine = new FakeEngine();
        using var h = Harness(new FakeChat().Answer(_ => Code(engine.SampleCode)), new FakeVoice(), engine);
        var style = WithMusic(h);
        File.Delete(style.Music.File);

        (await Should.ThrowAsync<VideoException>(() => MakeAsync(h, style))).Code.ShouldBe("music");
    }

    [AvaloniaFact]
    public async Task MusicOffChangesNothingButTheEmptyAssetRecord()
    {
        var engine = new FakeEngine();
        using var h = Harness(new FakeChat().Answer(_ => Code(engine.SampleCode)), new FakeVoice(), engine);
        var media = (FakeMedia)h.Get<IMediaTools>();

        var (_, video) = await MakeAsync(h, new VideoStyle { Name = "Sessiz müzik" });

        media.Overlays.Single().Overlay.Music.ShouldBeNull();
        media.MusicRead.ShouldBeEmpty();
        var folder = Folder(h, video);
        Directory.Exists(Path.Combine(folder, VideoFolders.Music)).ShouldBeFalse();
        (await AssetLedger.ReadAsync(folder, CancellationToken.None)).ShouldBeEmpty();
        File.Exists(Path.Combine(folder, VideoFolders.AssetsFile)).ShouldBeTrue();
    }
}
