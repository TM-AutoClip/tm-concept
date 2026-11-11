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
using TMConcept.Core.Render;
using TMConcept.Core.Speech;
using TMConcept.Core.Videos;
using TMConcept.Desktop.Dialogs;
using TMConcept.Desktop.Startup;
using TMConcept.Desktop.ViewModels;
using TMConcept.Tests.Providers;
using static TMConcept.Tests.Desktop.VideoPageTests;

namespace TMConcept.Tests.Desktop;

/// <summary>New versions of a made video: a new voice rendered with the kept code (no model call), new code for another engine (paid).</summary>
public sealed class VersionTests
{
    private const string Content = "[on screen: a map]\nIn 672 the city was under siege.\n[on screen: burning ships]\nA strange fire burned on the water.";

    private const string RemotionCode = "export const Scene = () => null;";

    private static ChatResponse Code(string code) => new("```js\n" + code + "\n```", new ChatUsage(500, 0, 0, 800), null, "end_turn", "r");

    private static DesktopHarness Harness(FakeChat chat, FakeVoice voice, params FakeEngine[] engines) => new(s =>
    {
        s.AddSingleton<IChatClientFactory>(chat);
        s.RemoveAll<IVideoEngine>();
        foreach (var engine in engines)
        {
            s.AddSingleton<IVideoEngine>(engine);
        }

        s.AddSingleton<IMediaTools, FakeMedia>();
        s.RemoveAll<ISpeechEngine>();
        s.AddSingleton<ISpeechEngine>(voice);
        s.AddSingleton<IConfirmDialog>(new FakeConfirm());
    });

    /// <summary>A profile, a concept and its first video (v1, voiced by the fake voice).</summary>
    private static async Task<(Profile Profile, VideoRecord First, ResolvedModel Model)> FirstAsync(DesktopHarness h, FakeVoice voice, string content = Content)
    {
        await FirstRunTests.CreateAsync(h);
        var profile = new Profile { Name = "Byzantium", ContentLanguage = ContentLanguages.English, ConceptText = "x" };
        await h.Get<IProfileStore>().SaveAsync(profile, CancellationToken.None);
        await h.Get<AppSession>().SelectProfileAsync(profile, CancellationToken.None);
        var key = new ApiKey { Provider = "anthropic", Label = "k", Secret = "fake-test-key-video-0123" };
        await h.Get<IApiKeyStore>().SaveAsync(key, CancellationToken.None);
        await h.Get<IStepModelStore>().SaveAsync(new StepModelChoice { Step = PipelineSteps.VideoCode, ProfileId = profile.Id, Provider = "anthropic",
            Model = "claude-sonnet-5-5" }, CancellationToken.None);
        await h.Get<IStepModelStore>().SaveAsync(new StepModelChoice { Step = PipelineSteps.Speech, ProfileId = profile.Id, Provider = ModelProviders.Local,
            Model = voice.Code }, CancellationToken.None);
        var concept = new Concept { ProfileId = profile.Id, Title = "Greek fire", Summary = "s", Content = content, Language = "en", Status = ConceptStatus.Queued };
        await h.Get<IConceptStore>().AddAsync([concept], CancellationToken.None);
        var model = new ResolvedModel(h.Get<IModelCatalog>().Find("anthropic", "claude-sonnet-5-5")!, key, string.Empty);
        var stored = (await h.Get<IConceptStore>().GetAsync(concept.Id, CancellationToken.None))!;
        var first = await h.Get<VideoPipeline>().RunAsync(new VideoJob(stored, profile, new VideoOptions { Speech = true, Subtitles = true }, model), null,
            CancellationToken.None);
        return (profile, first, model);
    }

    private static string Folder(DesktopHarness h, VideoRecord record) => LibraryPaths.Resolve(h.Get<IAppPaths>().LibraryDirectory, record.Folder);

    [AvaloniaFact]
    public async Task ANewVoiceIsAVersionRenderedWithTheKeptCodeTimedToTheNewClipsWithoutAModelCall()
    {
        var engine = new FakeEngine();
        var voice = new FakeVoice();
        var chat = new FakeChat().Answer(_ => Code(engine.SampleCode));
        using var h = Harness(chat, voice, engine);
        var (profile, first, _) = await FirstAsync(h, voice);
        var remaker = h.Get<VideoRemaker>();
        var source = remaker.Load(first);
        source.Texts.ShouldBe(["In 672 the city was under siege.", "A strange fire burned on the water."]);
        voice.Requests.Clear();

        var setup = await h.Get<SpeechService>().SetupAsync(new StepModelChoice { Step = PipelineSteps.Speech, Provider = ModelProviders.Local, Model = voice.Code },
            ContentLanguages.English, CancellationToken.None);
        var second = await remaker.RevoiceAsync(new RevoiceJob(source, profile,
            ["In 672 the city was besieged. [pause 2 s] The walls held for months.", "[slow] A strange fire [hard cut] burned on the water."], setup),
            null, CancellationToken.None);

        chat.Requests.Count.ShouldBe(1, "the kept code is rendered again: no model call");
        second.TotalUsd.ShouldBe(0, "a local voice costs nothing");
        var v1 = Folder(h, first);
        var v2 = Folder(h, second);
        Path.GetFileName(v2).ShouldBe("v2");
        Path.GetDirectoryName(v2).ShouldBe(Path.GetDirectoryName(v1), "next to the first version");
        File.Exists(Path.Combine(v1, VideoFolders.VideoFile)).ShouldBeTrue("the old version stays");
        File.ReadAllText(Path.Combine(v2, VideoFolders.Source, engine.CodeFile)).Trim().ShouldBe(engine.SampleCode, "the same code");

        // The voice read the parts between the markers; [slow] slowed the second scene; [hard cut] is not spoken, it is how the scene begins.
        voice.Requests.Select(r => r.Text).ShouldBe(["In 672 the city was besieged.", "The walls held for months.", "A strange fire burned on the water."]);
        voice.Requests[2].Speed.ShouldBe(setup.Speed * TMConcept.Core.Markers.MarkerCatalog.SlowSpeed, 0.001);

        // input.json: the captions are the spoken texts, the first scene lasts its two clips and the 2 s pause.
        var input = RenderInputBuilder.FromJson(File.ReadAllText(Path.Combine(v2, VideoFolders.Source, VideoFolders.InputFile)))!;
        input.Scenes[0].Caption.ShouldBe("In 672 the city was besieged. The walls held for months.");
        input.Scenes[1].Caption.ShouldBe("A strange fire burned on the water.");
        input.Scenes[0].DurationSeconds.ShouldBeGreaterThanOrEqualTo(2.2);
        input.Scenes[1].Transition.ShouldBe(TMConcept.Core.Scenes.SceneTransitions.Cut);
        engine.Input!.Scenes[0].Caption.ShouldBe(input.Scenes[0].Caption, "the render got the new input");
        File.ReadAllText(Path.Combine(v2, VideoFolders.SubtitlesFile)).ShouldContain("The walls held for months.");
        File.ReadAllText(Path.Combine(v2, VideoFolders.ScriptFile)).ShouldContain("[pause 2 s]", Case.Sensitive, "the next edit starts with the markers");
        Directory.GetFiles(Path.Combine(v2, VideoFolders.Audio)).Select(Path.GetFileName).ShouldBe(["1.wav", "2.wav"], ignoreOrder: true);
        WaveFile.DurationMs(Path.Combine(v2, VideoFolders.Audio, "1.wav")).ShouldBe(2200);
        remaker.Load(second).Texts[0].ShouldContain("[pause 2 s]");
        second.ConceptId.ShouldBe(first.ConceptId);
        second.Warnings.ShouldContain(VideoWarnings.AiVoice);
    }

    [AvaloniaFact]
    public async Task TheFirstVideoAppliesThePlaceholdersOfTheConceptTextExactly()
    {
        const string marked = "[on screen: a map]\nIn 672 the city was under siege. [pause 1 s] The walls held.\n[soft transition]\n"
            + "[on screen: burning ships]\n[slow] A strange fire burned on the water.";
        var engine = new FakeEngine();
        var voice = new FakeVoice();
        var chat = new FakeChat().Answer(_ => Code(engine.SampleCode));
        using var h = Harness(chat, voice, engine);

        var (_, first, _) = await FirstAsync(h, voice, marked);

        // The voice: the parts between the placeholders, the slow one slower; nothing in brackets is spoken.
        voice.Requests.Select(r => r.Text).ShouldBe(["In 672 the city was under siege.", "The walls held.", "A strange fire burned on the water."]);
        voice.Requests[2].Speed.ShouldBe(Math.Clamp(voice.Requests[0].Speed * TMConcept.Core.Markers.MarkerCatalog.SlowSpeed, ProfileLimits.SpeedMin,
            ProfileLimits.SpeedMax), 0.001);
        var v1 = Folder(h, first);
        WaveFile.DurationMs(Path.Combine(v1, VideoFolders.Audio, "1.wav")).ShouldBe(1200, "two parts of 100 ms and the 1 s pause");

        // The scenes: the pause is in the first scene's length, the second begins with a fade; captions and subtitles have no placeholder.
        var input = RenderInputBuilder.FromJson(File.ReadAllText(Path.Combine(v1, VideoFolders.Source, VideoFolders.InputFile)))!;
        input.Scenes.Count.ShouldBe(2, "the placeholder line is no scene");
        input.Scenes[0].Caption.ShouldBe("In 672 the city was under siege. The walls held.");
        input.Scenes[0].DurationSeconds.ShouldBeGreaterThanOrEqualTo(1.2);
        input.Scenes[1].Transition.ShouldBe(TMConcept.Core.Scenes.SceneTransitions.Fade);
        File.ReadAllText(Path.Combine(v1, VideoFolders.SubtitlesFile)).ShouldNotContain("[");
        chat.Requests[0].User.ShouldContain("caption «A strange fire burned on the water.»; fade", Case.Sensitive, "the code is told not to animate one");

        // ffmpeg fades through black at the second scene's start, whatever the code drew.
        var media = (FakeMedia)h.Get<IMediaTools>();
        media.Fades.Single().ShouldBe([(long)Math.Round(input.Scenes[1].StartSeconds * 1000)]);

        // A new version starts from the text with its placeholders, the transition at the start of its scene.
        var texts = h.Get<VideoRemaker>().Load(first).Texts;
        texts[0].ShouldBe("In 672 the city was under siege. [pause 1 s] The walls held.");
        texts[1].ShouldBe("[soft transition] [slow] A strange fire burned on the water.");
    }

    [Fact]
    public void ASceneWithoutAVoiceLastsAsLongAsItsTextAndItsPauses()
    {
        var plain = VideoCostEstimator.Milliseconds("The walls held for months.", ContentLanguages.English);

        VideoRemaker.SilentMs("The walls held [pause 2 s] for months. [soft transition]", ContentLanguages.English).ShouldBe(plain + 2000);
        VideoRemaker.WithTransition(TMConcept.Core.Scenes.SceneTransitions.Fade, "Text.", ContentLanguages.Turkish).ShouldBe("[yumuşak geçiş] Text.");
        VideoRemaker.WithTransition(TMConcept.Core.Scenes.SceneTransitions.None, "Text.", ContentLanguages.Turkish).ShouldBe("Text.");
    }

    [AvaloniaFact]
    public async Task AnotherEngineGetsNewPaidCodeAndUsesTheKeptClipsAndImages()
    {
        var hyper = new FakeEngine();
        var remotion = new FakeEngine { Kind = EngineKind.Remotion, Name = "Fake Remotion" };
        var voice = new FakeVoice();
        var chat = new FakeChat().Answer(_ => Code(hyper.SampleCode)).Answer(_ => Code(RemotionCode));
        using var h = Harness(chat, voice, hyper, remotion);
        var (profile, first, model) = await FirstAsync(h, voice);
        var remaker = h.Get<VideoRemaker>();
        voice.Requests.Clear();

        var second = await remaker.RecodeAsync(new RecodeJob(remaker.Load(first), profile, remotion, model), null, CancellationToken.None);

        chat.Requests.Count.ShouldBe(2, "one new code call");
        voice.Requests.ShouldBeEmpty("the clips are used again");
        second.Engine.ShouldBe("Fake Remotion");
        second.TotalUsd.ShouldBeGreaterThan(0);
        VideoRemaker.Options(second).Engine.ShouldBe(EngineKind.Remotion);
        var v2 = Folder(h, second);
        File.ReadAllText(Path.Combine(v2, VideoFolders.Source, remotion.CodeFile)).Trim().ShouldBe(RemotionCode);
        Directory.GetFiles(Path.Combine(v2, VideoFolders.Audio)).Select(Path.GetFileName).ShouldBe(["1.wav", "2.wav"], ignoreOrder: true);
        (await h.Get<IVideoStore>().CostsAsync(second.Id, CancellationToken.None)).ShouldAllBe(c => c.Step == PipelineSteps.VideoCode);
    }

    [AvaloniaFact]
    public async Task ThePageShowsOneRowWithItsVersionsAndTheEditorMakesAVersionThatIsSelected()
    {
        var engine = new FakeEngine();
        var voice = new FakeVoice();
        var chat = new FakeChat().Answer(_ => Code(engine.SampleCode));
        using var h = Harness(chat, voice, engine);
        await FirstAsync(h, voice);
        var page = h.Get<ProducedViewModel>();
        await page.ActivateAsync();
        page.Selected = page.Items.Single();
        var editor = page.Editor;
        for (var i = 0; i < 100 && !editor.CanEdit; i++)
        {
            await Task.Delay(20);
        }

        editor.Scenes.Count.ShouldBe(2);
        editor.Placeholders.Select(m => TMConcept.Core.Markers.MarkerText.Find(m.Token)!.Id).ShouldBe(TMConcept.Core.Markers.MarkerCatalog.All.Select(m => m.Id),
            "pauses, speed and transitions: all applied by this computer");
        editor.Placeholders.ShouldAllBe(m => m.Hint.Length > 0);
        editor.RecodeEstimate.ShouldNotBeEmpty();

        // A silent new version (voice off) with an edited text: nothing to install, nothing paid.
        var speech = editor.Picker.Row(PipelineSteps.Speech)!;
        speech.Selected = speech.Options[0];
        editor.Scenes[0].Text = "The city held. [pause 1 s] For months.";
        editor.Scenes[0].Changed.ShouldBeTrue();
        await editor.RevoiceCommand.ExecuteAsync(null);
        for (var i = 0; i < 100 && page.Versions.Count < 2; i++)
        {
            await Task.Delay(20);
        }

        editor.Status.ShouldContain("v2");

        // The version's steps were shown as a video's are: a new voice that is off renders the kept code, so no code call and no narration.
        editor.Progress.Running.ShouldBeFalse();
        editor.Progress.Stages.Where(s => s.Code is VideoSteps.Code or VideoSteps.Speech or VideoSteps.Images).ShouldAllBe(s => s.State == StageState.Skipped);
        editor.Progress.Stages.Single(s => s.Code == VideoSteps.Render).State.ShouldBe(StageState.Done);
        editor.Progress.SpentUsd.ShouldBe(0);
        page.Items.Count.ShouldBe(1, "one row per video");
        page.Versions.Select(v => v.Version).ShouldBe([1, 2]);
        page.SelectedVersion!.Version.ShouldBe(2, "the new version is shown");
        page.SelectedVersion.Video.Warnings.ShouldContain(VideoWarnings.SpeechOff);

        // The first version can be chosen and deleted; the second stays.
        page.SelectedVersion = page.Versions[0];
        await page.DeleteAsync();
        page.Items.Count.ShouldBe(1);
        page.Versions.Select(v => v.Version).ShouldBe([2]);
    }

    [Fact]
    public void PartsAndPausesAreJoinedIntoOneClipOfOneFormat()
    {
        var folder = Path.Combine(Path.GetTempPath(), "tmconcept-tests", "wave-" + Guid.NewGuid().ToString("N"));
        try
        {
            var a = Path.Combine(folder, "a.wav");
            var b = Path.Combine(folder, "b.wav");
            WaveFile.WritePcm16(a, new float[24_000], 24_000);
            WaveFile.WritePcm16(b, new float[12_000], 24_000);

            WaveFile.Join([(null, 500), (a, 1000), (b, 250)], Path.Combine(folder, "joined.wav")).ShouldBe(500 + 1000 + 1000 + 500 + 250);
            WaveFile.Join([(null, 700)], Path.Combine(folder, "silent.wav")).ShouldBe(700);

            var other = Path.Combine(folder, "other.wav");
            WaveFile.WritePcm16(other, new float[100], 16_000);
            Should.Throw<InvalidDataException>(() => WaveFile.Join([(a, 0), (other, 0)], Path.Combine(folder, "bad.wav")));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
