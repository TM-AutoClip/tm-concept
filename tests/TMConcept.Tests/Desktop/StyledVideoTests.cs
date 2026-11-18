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
/// A video made with a template (0.1.19), end to end with fakes (no real model, voice or ffmpeg): the input's tokens and the project's font,
/// the template check with its single repair, colours replaced without a repair, the spoken closing and the closing card with the channel
/// photo, and a new version that keeps the snapshot.
/// </summary>
public sealed class StyledVideoTests
{
    private const string Content = "[on screen: a map]\nIn 672 the city was under siege.\n[on screen: burning ships]\nA strange fire burned on the water.";

    private static ChatResponse Code(string code) => new("```js\n" + code + "\n```", new ChatUsage(500, 0, 0, 800), null, "end_turn", "r");

    private static VideoStyle Template() => new()
    {
        Name = "Kırmızı",
        Subtitles = true,
        ProgressBar = true,
        FontId = "poppins",
        PaletteId = "crimson",
        DrawStyle = DrawStyle.LineArt,
        Closing = ClosingPlacement.Both,
        ClosingPhrase = ClosingPhrase.Short,
        ClosingPlatform = ClosingPlatform.Instagram,
    };

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

    /// <summary>A profile (with a channel photo when asked), a concept and its first video made with <paramref name="style"/>.</summary>
    private static async Task<(Profile Profile, VideoRecord Video)> MakeAsync(DesktopHarness h, VideoStyle? style, bool photo = true, bool speech = true)
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
        if (photo)
        {
            // Stored like a profile image: relative to the library folder.
            var library = h.Get<IAppPaths>().LibraryDirectory;
            Directory.CreateDirectory(Path.Combine(library, "Byzantium"));
            await File.WriteAllBytesAsync(Path.Combine(library, "Byzantium", "photo.png"), [0x89, 0x50, 0x4E, 0x47], CancellationToken.None);
            profile.ChannelPhoto = "Byzantium/photo.png";
        }

        var options = new VideoOptions { Speech = speech };
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
    public async Task ATemplateVideoGetsItsTokensFontCheckedCodeClosingAndOverlay()
    {
        var engine = new FakeEngine();
        var voice = new FakeVoice();
        var chat = new FakeChat().Answer(_ => Code(engine.SampleCode + "\ndocument.getElementById(\"root\").style.color = \"#00FF00\";"));
        using var h = Harness(chat, voice, engine);

        var (_, video) = await MakeAsync(h, Template());

        // The prompt has the template block; the input the code reads has the palette and the font; the font is in the project.
        chat.Requests.Single().System.ShouldContain("Template (the user's choice");
        chat.Requests.Single().System.ShouldContain("ONLY input.style.font (\"Poppins\")");
        var style = engine.Input!.Style.ShouldNotBeNull();
        style.Colors.Accent.ShouldBe("#E5383B");
        engine.Input.Theme.Accent.ShouldBe("#E5383B", "kept code that reads only theme follows the palette");
        style.EndCardSeconds.ShouldBe(StyleLayout.EndCardSeconds);
        engine.Fonts.ShouldBe(["Poppins-Regular.ttf", "Poppins-Bold.ttf", "Poppins-Italic.ttf", "Poppins-BoldItalic.ttf"], ignoreOrder: true);

        // The green outside the palette was replaced without a repair (one call).
        var folder = Folder(h, video);
        var kept = File.ReadAllText(Path.Combine(folder, VideoFolders.Source, engine.CodeFile));
        kept.ShouldNotContain("#00FF00");
        kept.ShouldContain("style.color = \"#");
        video.Warnings.ShouldNotContain(VideoWarnings.Repaired);
        Directory.Exists(Path.Combine(folder, VideoFolders.Source, "fonts")).ShouldBeFalse("the app's fonts are not kept with every version");

        // The spoken closing ends the last scene: the voice says it, the subtitles and the kept narration have it.
        voice.Requests.Last().Text.ShouldEndWith("Don't forget to follow!");
        File.ReadAllText(Path.Combine(folder, VideoFolders.SubtitlesFile)).ShouldContain("Don't forget to follow!");
        File.ReadAllText(Path.Combine(folder, VideoFolders.NarrationFile)).ShouldContain("Don't forget to follow!");
        h.Get<VideoRemaker>().Load(video).Texts[^1].ShouldEndWith("burned on the water. Don't forget to follow!", customMessage: "the kept script has it");

        // The overlay: subtitles, the bar and the closing card in the template's font, with the photo laid over the card.
        var (overlay, ass, files) = ((FakeMedia)h.Get<IMediaTools>()).Overlays.Single();
        overlay.AssFile.ShouldBe("subtitles.ass");
        overlay.FontsFolder.ShouldBe(VideoFontFiles.Folder);
        files.ShouldContain("fonts/Poppins-Bold.ttf");
        files.ShouldContain("channel-photo.png");
        var photo = overlay.Photo.ShouldNotBeNull();
        photo.File.ShouldBe("channel-photo.png");
        photo.StartSeconds.ShouldBe(Math.Round(engine.Input.DurationSeconds - StyleLayout.EndCardSeconds, 3), "the last seconds");
        var last = engine.Input.Scenes[^1];
        last.DurationSeconds.ShouldBeGreaterThanOrEqualTo(StyleLayout.EndCardSeconds + (TMConcept.Core.Render.RenderTiming.ClosingMs / 1000.0),
            "the last scene got the card's hold");
        (last.StartSeconds + 0.1).ShouldBeLessThanOrEqualTo(photo.StartSeconds, "the card covers only the hold added after the narration (a 100 ms clip)");
        var text = ass.ShouldNotBeNull();
        text.ShouldContain("Style: Default,Poppins,");
        text.ShouldContain(@"\clip(0,0,0,");
        text.ShouldContain("Byzantium");
        text.ShouldContain("Follow");
        text.ShouldContain("siege");

        // The video keeps its snapshot.
        var options = VideoRemaker.Options(video);
        options.Style.ShouldNotBeNull().PaletteId.ShouldBe("crimson");
        options.Subtitles.ShouldBeTrue();
    }

    [AvaloniaFact]
    public async Task WithoutAChannelPhotoTheCardIsDrawnWithoutIt()
    {
        var engine = new FakeEngine();
        using var h = Harness(new FakeChat().Answer(_ => Code(engine.SampleCode)), new FakeVoice(), engine);

        await MakeAsync(h, Template(), photo: false);

        var (overlay, ass, _) = ((FakeMedia)h.Get<IMediaTools>()).Overlays.Single();
        overlay.Photo.ShouldBeNull();
        var text = ass.ShouldNotBeNull();
        text.ShouldContain("Don't forget to follow!");
        text.ShouldNotContain("Dialogue: 11,", customMessage: "no ring without a photo");
    }

    [AvaloniaFact]
    public async Task AProgressBarDrawnByTheCodeGetsOneRepair()
    {
        var engine = new FakeEngine();
        var chat = new FakeChat()
            .Answer(_ => Code(engine.SampleCode + "\nconst progressBar = document.createElement(\"div\");"))
            .Answer(_ => Code(engine.SampleCode));
        using var h = Harness(chat, new FakeVoice(), engine);

        var (_, video) = await MakeAsync(h, Template());

        chat.Requests.Count.ShouldBe(2);
        chat.Requests[1].User.ShouldContain("progress bar, timer or scene counter drawn by the code");
        chat.Requests[1].System.ShouldBe(chat.Requests[0].System, "the repair keeps the template block");
        video.Warnings.ShouldContain(VideoWarnings.Repaired);
    }

    [AvaloniaFact]
    public async Task ACodeThatBreaksTheTemplateTwiceFailsTheVideoWithAClearError()
    {
        var engine = new FakeEngine();
        var bad = Code(engine.SampleCode + "\nroot.style.fontFamily = \"Arial\";");
        var chat = new FakeChat().Answer(_ => bad).Answer(_ => bad);
        using var h = Harness(chat, new FakeVoice(), engine);

        var error = await Should.ThrowAsync<VideoException>(() => MakeAsync(h, Template()));

        error.Code.ShouldBe("code");
        error.Message.ShouldStartWith("The video code still did not follow the template after the repair and was not run:");
        error.Message.ShouldContain("font \"Arial\" is not the template font");
        engine.Renders.ShouldBe(0, "a code that breaks the template is never rendered");
    }

    [AvaloniaFact]
    public async Task ASafetyFindingAndATemplateBreakAreRepairedTogether()
    {
        var engine = new FakeEngine();
        var chat = new FakeChat()
            .Answer(_ => Code(engine.SampleCode + "\nconst top = window.top;\nconst progressBar = document.createElement(\"div\");"))
            .Answer(_ => Code(engine.SampleCode));
        using var h = Harness(chat, new FakeVoice(), engine);

        var (_, video) = await MakeAsync(h, Template());

        chat.Requests.Count.ShouldBe(2, "one repair for both");
        var repair = chat.Requests[1].User;
        repair.ShouldContain("progress bar, timer or scene counter drawn by the code", customMessage: "the template finding is in the repair too");
        engine.Renders.ShouldBe(1);
        video.Warnings.ShouldContain(VideoWarnings.Repaired);
    }

    [AvaloniaFact]
    public async Task ARepairThatFixesTheSafetyFindingButNotTheTemplateNamesTheTemplate()
    {
        var engine = new FakeEngine();
        var chat = new FakeChat()
            .Answer(_ => Code(engine.SampleCode + "\nconst top = window.top;"))
            .Answer(_ => Code(engine.SampleCode + "\nroot.style.fontFamily = \"Arial\";"));
        using var h = Harness(chat, new FakeVoice(), engine);

        var error = await Should.ThrowAsync<VideoException>(() => MakeAsync(h, Template()));

        error.Message.ShouldStartWith("The video code still did not follow the template after the repair and was not run:");
        chat.Requests.Count.ShouldBe(2);
    }

    [AvaloniaFact]
    public async Task ASpokenClosingWithNarrationOffIsShownOnTheCard()
    {
        var engine = new FakeEngine();
        var voice = new FakeVoice();
        using var h = Harness(new FakeChat().Answer(_ => Code(engine.SampleCode)), voice, engine);
        var template = Template();
        template.Closing = ClosingPlacement.Voice;

        var (_, video) = await MakeAsync(h, template, speech: false);

        voice.Requests.ShouldBeEmpty();
        File.ReadAllText(Path.Combine(Folder(h, video), VideoFolders.NarrationFile)).ShouldNotContain("Don't forget to follow!", customMessage: "nothing silent added");
        var (_, ass, _) = ((FakeMedia)h.Get<IMediaTools>()).Overlays.Single();
        ass.ShouldNotBeNull().ShouldContain("Don't forget to follow!", customMessage: "on the card instead");
        engine.Input!.Style.ShouldNotBeNull().EndCardSeconds.ShouldBe(StyleLayout.EndCardSeconds);
        VideoRemaker.Options(video).Style.ShouldNotBeNull().Closing.ShouldBe(ClosingPlacement.Screen, "the video's snapshot says what it shows");
        template.Closing.ShouldBe(ClosingPlacement.Voice);
    }

    [AvaloniaFact]
    public async Task ANewVoiceKeepsTheSnapshotAndSaysTheClosingOnce()
    {
        var engine = new FakeEngine();
        var voice = new FakeVoice();
        using var h = Harness(new FakeChat().Answer(_ => Code(engine.SampleCode)), voice, engine);
        var (profile, first) = await MakeAsync(h, Template());
        var remaker = h.Get<VideoRemaker>();
        var source = remaker.Load(first);
        source.Texts[^1].ShouldEndWith("Don't forget to follow!");
        voice.Requests.Clear();

        var setup = await h.Get<SpeechService>().SetupAsync(new StepModelChoice { Step = PipelineSteps.Speech, Provider = ModelProviders.Local, Model = voice.Code },
            ContentLanguages.English, CancellationToken.None);
        var second = await remaker.RevoiceAsync(new RevoiceJob(source, profile, source.Texts, setup), null, CancellationToken.None);

        // The voice may come from its cache (same text); the version's narration says what is spoken: the closing once.
        var narration = File.ReadAllText(Path.Combine(Folder(h, second), VideoFolders.NarrationFile));
        narration.Split("Don't forget to follow!").Length.ShouldBe(2, "said once");
        string.Join(" ", voice.Requests.Select(r => r.Text)).Split("Don't forget to follow!").Length.ShouldBeLessThanOrEqualTo(2);
        var overlays = ((FakeMedia)h.Get<IMediaTools>()).Overlays;
        overlays.Count.ShouldBe(2);
        overlays[1].AssText!.ShouldContain(@"\clip(0,0,0,", customMessage: "the version keeps the template's progress bar");
        engine.Input!.Style.ShouldNotBeNull().Font.ShouldBe("Poppins");
        engine.Fonts.ShouldContain("Poppins-Regular.ttf", "the font is placed again for the kept code");
    }

    [AvaloniaFact]
    public async Task AVideoWithoutATemplateIsMadeAsBefore()
    {
        var engine = new FakeEngine();
        var chat = new FakeChat().Answer(_ => Code(engine.SampleCode + "\nconst progressBar = 1; document.body.style.color = \"#00FF00\";"));
        using var h = Harness(chat, new FakeVoice(), engine);

        var (_, video) = await MakeAsync(h, null);

        chat.Requests.Single().System.ShouldNotContain("Template (");
        engine.Input!.Style.ShouldBeNull();
        engine.Fonts.ShouldBeEmpty();
        File.ReadAllText(Path.Combine(Folder(h, video), VideoFolders.Source, engine.CodeFile)).ShouldContain("#00FF00", customMessage: "no template, no check");
        var (overlay, ass, _) = ((FakeMedia)h.Get<IMediaTools>()).Overlays.Single();
        overlay.ShouldBe(new MuxOverlay("subtitles.ass"));
        ass!.ShouldContain("Style: Default,DejaVu Sans,", customMessage: "the old subtitles");
        File.ReadAllText(Path.Combine(Folder(h, video), VideoFolders.SubtitlesFile)).ShouldNotContain("follow");
    }
}
