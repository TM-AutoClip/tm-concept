using Avalonia.Headless.XUnit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Shouldly;
using TMConcept.Core.Activity;
using TMConcept.Core.Components;
using TMConcept.Core.Models;
using TMConcept.Core.Music;
using TMConcept.Core.Profiles;
using TMConcept.Core.Styles;
using TMConcept.Core.Videos;
using TMConcept.Desktop.Dialogs;
using TMConcept.Desktop.Localization;
using TMConcept.Desktop.ViewModels;

namespace TMConcept.Tests.Desktop;

/// <summary>
/// 0.1.54: the "Müzik üret" page and "Müziklerim" (a piece made from the user's own words, with a reference sound's measured tempo, goes into
/// the collection; an own file is copied in; a removed piece's file is deleted for good), and the profile's music and reference on the video
/// page's model choice (kept per profile, put into each starting video's template copy, never into the template). Fake music models and audio:
/// nothing reaches the network, no model runs.
/// </summary>
public sealed class MusicPageTests
{
    private const int Rate = MusicLibraryRules.SampleRate;

    /// <summary>30 seconds of clicks at 120 beats per minute.</summary>
    private static float[] Clicks()
    {
        var samples = new float[Rate * 30];
        for (var beat = 0; beat * Rate / 2 < samples.Length; beat++)
        {
            var at = beat * Rate / 2;
            for (var i = 0; i < 400 && at + i < samples.Length; i++)
            {
                samples[at + i] = (float)(Math.Exp(-i / 60.0) * (i % 2 == 0 ? 1 : -1));
            }
        }

        return samples;
    }

    private static DesktopHarness Harness(FakeGenerator generator, FakeConfirm? confirm = null) => new(s =>
    {
        s.AddSingleton<IConfirmDialog>(confirm ?? new FakeConfirm());

        // ffmpeg counts as installed (the reference sound is read by the fake audio below).
        s.RemoveAll<IComponentSource>();
        s.AddSingleton<IComponentSource>(new FakeComponents(new FakeComponent(ComponentCodes.Ffmpeg, ready: true)));
        s.RemoveAll<IMusicAudio>();
        s.AddSingleton<IMusicAudio>(new FakeAudio(Clicks()));
        s.RemoveAll<IMusicGenerators>();
        s.AddSingleton<IMusicGenerators>(sp => new OnlyFake(new MusicGenerators(sp.GetRequiredService<IModelCatalog>(), [],
            sp.GetRequiredService<StepModelResolver>()), generator));
    });

    [AvaloniaFact]
    public async Task APieceMadeFromTheUsersWordsAndAReferenceTempoGoesIntoMyMusic()
    {
        var generator = new FakeGenerator();
        using var h = Harness(generator);
        await FirstRunTests.CreateAsync(h);
        var text = h.Get<Localizer>();
        var reference = Path.Combine(h.Paths.DataDirectory, "ref.wav");
        await File.WriteAllBytesAsync(reference, [1, 2, 3]);
        var page = h.Get<MusicViewModel>();
        await page.ActivateAsync();

        page.Models.ShouldNotBeEmpty();
        page.Model = page.Models.First(m => m.Local);
        page.Model.Ready.ShouldBeTrue("the fake local model counts as installed");
        page.EstimateText.ShouldBe(text.Format("music.estimate.free", 60));
        page.GenerateCommand.CanExecute(null).ShouldBeFalse("nothing written yet");
        page.Prompt = "antik Mısır'da disko";
        page.ReferenceFile = reference;
        page.GenerateCommand.CanExecute(null).ShouldBeTrue();

        await page.GenerateCommand.ExecuteAsync(null);

        var expected = (int)Math.Round(BeatTracker.Analyze(Clicks(), Rate)!.Bpm);
        var asked = generator.Requests.ShouldHaveSingleItem();
        asked.Prompt.ShouldStartWith("antik Mısır'da disko, about " + expected + " BPM");
        asked.Prompt.ShouldContain("instrumental only");
        asked.Bpm.ShouldBe(expected);
        asked.Seconds.ShouldBe(60);
        var piece = page.Pieces.ShouldHaveSingleItem();
        piece.Title.ShouldBe("antik Mısır'da disko");
        piece.Entry.Generated.ShouldBeTrue();
        piece.Entry.ReferenceName.ShouldBe("ref.wav");
        piece.Entry.Track.Id.ShouldStartWith(MusicLibraryRules.MinePrefix);
        piece.Entry.Track.License.ShouldStartWith(AssetLicenses.GeneratedPrefix);
        File.Exists(piece.Entry.Track.File).ShouldBeTrue();
        piece.Entry.Track.File.ShouldStartWith(h.Get<IMyMusic>().Folder);
        page.Status.ShouldBe(text.Format("music.made", "antik Mısır'da disko"));
        page.Working.ShouldBeFalse();
        Directory.Exists(Path.Combine(h.Paths.WorkDirectory, MusicMaker.WorkFolder)).ShouldBeTrue();
        Directory.EnumerateDirectories(Path.Combine(h.Paths.WorkDirectory, MusicMaker.WorkFolder)).ShouldBeEmpty("the work folder of the piece is removed");

        // The video page's music choice takes it from "Müziklerim".
        var media = h.Get<VideoMediaViewModel>();
        await media.LoadAsync(new VideoMedia());
        media.MusicSources.ShouldContain(c => c.Value == (int)MusicSource.MyMusic && c.Label == text["style.music.mymusic"]);
        media.MusicSource = media.MusicSources.Single(c => c.Value == (int)MusicSource.MyMusic);
        media.MusicMine.ShouldBeTrue();
        media.MyTrack.ShouldNotBeNull().Track!.Id.ShouldBe(piece.Entry.Track.Id);
        var chosen = media.ToMedia();
        chosen.Music.Source.ShouldBe(MusicSource.MyMusic);
        chosen.Music.Track!.File.ShouldBe(piece.Entry.Track.File);
        VideoStyleRules.MediaProblems(chosen).ShouldBeEmpty();
    }

    [AvaloniaFact]
    public async Task AFailedPieceIsShownAndAddsNothing()
    {
        var generator = new FakeGenerator { Fail = true };
        using var h = Harness(generator);
        await FirstRunTests.CreateAsync(h);
        var page = h.Get<MusicViewModel>();
        await page.ActivateAsync();
        page.Model = page.Models.First(m => m.Local);
        page.Prompt = "calm sea";

        await page.GenerateCommand.ExecuteAsync(null);

        page.Pieces.ShouldBeEmpty();
        page.Status.ShouldContain("fake music failure");
        h.Get<IActivityFeed>().Recent().ShouldContain(l => l.Level == ActivityLevel.Error && l.Text.Contains("fake music failure", StringComparison.Ordinal));
    }

    [AvaloniaFact]
    public async Task AnOwnFileIsCopiedInAndARemovedPieceIsDeletedForGood()
    {
        var confirm = new FakeConfirm();
        using var h = Harness(new FakeGenerator(), confirm);
        await FirstRunTests.CreateAsync(h);
        var original = Path.Combine(h.Paths.DataDirectory, "user-music", "My Song.MP3");
        Directory.CreateDirectory(Path.GetDirectoryName(original)!);
        await File.WriteAllBytesAsync(original, [0x49, 0x44, 0x33]);
        var mine = h.Get<IMyMusic>();

        var entry = await mine.AddFileAsync(original, CancellationToken.None);

        entry.Generated.ShouldBeFalse();
        entry.Track.Title.ShouldBe("My Song");
        entry.Track.License.ShouldBe(AssetLicenses.OwnFile);
        entry.Track.File.ShouldEndWith(".mp3");
        File.Exists(original).ShouldBeTrue("the original is copied, not moved");
        var page = h.Get<MusicViewModel>();
        await page.ActivateAsync();
        var row = page.Pieces.ShouldHaveSingleItem();

        await page.RemoveCommand.ExecuteAsync(row);

        confirm.Dangers.ShouldBe([true]);
        page.Pieces.ShouldBeEmpty();
        File.Exists(entry.Track.File).ShouldBeFalse();
        (await mine.ListAsync(CancellationToken.None)).ShouldBeEmpty();
    }

    [AvaloniaFact]
    public async Task TheProfilesMusicAndReferenceAreKeptPerProfileAndReachTheVideoNotTheTemplate()
    {
        using var h = Harness(new FakeGenerator());
        await FirstRunTests.CreateAsync(h);
        var store = h.Get<IVideoMediaStore>();
        var one = new Profile { Name = "One" };
        var two = new Profile { Name = "Two" };
        await h.Get<IProfileStore>().SaveAsync(one, CancellationToken.None);
        await h.Get<IProfileStore>().SaveAsync(two, CancellationToken.None);
        var media = new VideoMedia
        {
            Music = new VideoMusic { Source = MusicSource.OwnFile, File = "C:/music/a.mp3", LevelDb = -20 },
            Reference = new TMConcept.Core.References.ReferenceTempoProfile { Source = "clip.mp4", CutsPer10Seconds = 6.3 },
        };

        await store.SaveAsync(one.Id, media, CancellationToken.None);

        var read = await store.GetAsync(one.Id, CancellationToken.None);
        read.Music.Source.ShouldBe(MusicSource.OwnFile);
        read.Music.LevelDb.ShouldBe(-20);
        read.Reference.ShouldNotBeNull().Source.ShouldBe("clip.mp4");
        (await store.GetAsync(two.Id, CancellationToken.None)).Music.Source.ShouldBe(MusicSource.Off, "another profile has its own (empty) choice");

        // A template saved before 0.1.54 with its own music: the video takes the profile's, the template keeps its JSON as it is.
        var template = new VideoStyle { Name = "Old", Music = new VideoMusic { Source = MusicSource.Library } };
        var options = new VideoOptions();
        VideoStyleRules.Snapshot(options, template, read);
        options.Style!.Music.Source.ShouldBe(MusicSource.OwnFile);
        options.Style.Reference.ShouldNotBeNull();
        template.Music.Source.ShouldBe(MusicSource.Library);
        VideoStyleRules.Snapshot(options, template, await store.GetAsync(two.Id, CancellationToken.None));
        options.Style!.Music.Source.ShouldBe(MusicSource.Off, "an empty choice: no music, whatever an old template held");
        options.Style.Reference.ShouldBeNull();

        VideoStyleRules.MediaProblems(new VideoMedia { Music = new VideoMusic { Source = MusicSource.MyMusic } })
            .ShouldBe([VideoStyleProblem.MusicFileMissing]);
    }

    [AvaloniaFact]
    public async Task TheTemplateEditorNoLongerHoldsMusicOrAReference()
    {
        using var h = new DesktopHarness();
        var old = new VideoStyle
        {
            Name = "Old",
            Music = new VideoMusic { Source = MusicSource.OwnFile, File = "C:/a.mp3" },
            Reference = new TMConcept.Core.References.ReferenceTempoProfile { Source = "clip.mp4" },
        };

        var editor = new VideoStyleEditorViewModel(old, false, h.Get<Localizer>());

        editor.Changed.ShouldBeFalse("opening an old template is no unsaved change");
        var saved = editor.ToStyle();
        saved.Music.Source.ShouldBe(MusicSource.Off);
        saved.Reference.ShouldBeNull();
        await Task.CompletedTask;
    }

    /// <summary>A music model that writes a small WAV file (or fails).</summary>
    internal sealed class FakeGenerator : IMusicGenerator
    {
        public List<MusicGenerationRequest> Requests { get; } = [];

        public bool Fail { get; init; }

        public bool Handles(CatalogModel model) => true;

        public bool Ready(CatalogModel model) => true;

        public async Task<GeneratedMusic> GenerateAsync(MusicGenerationRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            if (Fail)
            {
                throw new MusicGenerationException("fake music failure");
            }

            Directory.CreateDirectory(request.OutputFolder);
            var file = Path.Combine(request.OutputFolder, "piece.wav");
            await File.WriteAllBytesAsync(file, [0x52, 0x49, 0x46, 0x46], cancellationToken);
            return new GeneratedMusic(file, 0, true);
        }
    }

    /// <summary>The catalog's music models, every one ready and made by the fake.</summary>
    private sealed class OnlyFake(IMusicGenerators inner, FakeGenerator generator) : IMusicGenerators
    {
        public IReadOnlyList<CatalogModel> Models => inner.Models;

        public CatalogModel? Find(string key) => inner.Find(key);

        public bool Ready(CatalogModel model) => true;

        public Task<ResolvedModel> ResolveAsync(CatalogModel model, CancellationToken cancellationToken) =>
            Task.FromResult(new ResolvedModel(model, null!, string.Empty));

        public IMusicGenerator? For(CatalogModel model) => generator;
    }

    /// <summary>Audio that reads as the given samples.</summary>
    private sealed class FakeAudio(float[] samples) : IMusicAudio
    {
        public Task<float[]?> ReadMonoAsync(string file, int sampleRate, int maxSeconds, string work, CancellationToken cancellationToken) =>
            Task.FromResult<float[]?>(samples);

        public Task<bool> RenderBedAsync(string file, MusicBed bed, string output, CancellationToken cancellationToken) => Task.FromResult(false);
    }
}
