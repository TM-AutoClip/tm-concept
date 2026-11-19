using Avalonia.Headless.XUnit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Shouldly;
using TMConcept.Core.Chat;
using TMConcept.Core.Concepts;
using TMConcept.Core.Engines;
using TMConcept.Core.Images;
using TMConcept.Core.Keys;
using TMConcept.Core.Media;
using TMConcept.Core.Models;
using TMConcept.Core.Paths;
using TMConcept.Core.Pipeline;
using TMConcept.Core.Profiles;
using TMConcept.Core.Videos;
using TMConcept.Desktop.Startup;
using TMConcept.Tests.Providers;
using static TMConcept.Tests.Desktop.VideoPageTests;

namespace TMConcept.Tests.Desktop;

/// <summary>
/// The 19 Nov 2026 local run end to end, with fakes only (no model, no render, no network): a small local model writes a concept without
/// scenes (one repair with the findings gives it scenes), numbered image prompts, then video code that never shows the images (the app's
/// own animation is rendered at once, without a repair call). A local model whose rendered frames are empty is rendered again with the
/// app's own animation and warned about; an online model's empty video stops with the reason and no paid repair.
/// </summary>
public sealed class LocalModelPipelineTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    /// <summary>Valid HyperFrames code that draws nothing from the scenes (what the small model copied).</summary>
    private const string NoImages = "window.__timelines = window.__timelines || {}; window.__timelines[\"concept\"] = gsap.timeline({ paused: true });";

    private static ChatResponse Answer(string text) => new(text, new ChatUsage(500, 0, 0, 300), null, "stop", "r");

    private static ChatResponse Code(string code) => Answer("```js\n" + code + "\n```");

    private static string Prompts(int count) => "{\"prompts\":[" + string.Join(",", Enumerable.Range(1, count).Select(i => "{\"scene\":" + i + ",\"prompt\":\"picture " + i + "\"}")) + "]}";

    /// <summary>A frame with a gradient (not flat); the scene images look the same, so the frame check sees them.</summary>
    private static FramePixels Gradient() => new(16, 8, Enumerable.Range(0, 16 * 8).SelectMany(i => Enumerable.Repeat((byte)((i % 16 * 12) + (i / 16 * 6)), 3)).ToArray());

    private static FramePixels Flat() => new(16, 8, Enumerable.Repeat((byte)90, 16 * 8 * 3).ToArray());

    private static DesktopHarness Harness(FakeChat chat, FakeEngine engine, FakeImages images) => new(s =>
    {
        s.AddSingleton<IChatClientFactory>(chat);
        s.RemoveAll<IVideoEngine>();
        s.AddSingleton<IVideoEngine>(engine);
        s.AddSingleton<IMediaTools, FakeMedia>();
        s.RemoveAll<IImageEngine>();
        s.AddSingleton<IImageEngine>(images);
    });

    private static ResolvedModel Local() => new(TMConcept.Core.AppOllama.AppModelRegistry.Entry(TMConcept.Core.AppOllama.AppModelAdvisor.Find("qwen3.5:4b-q8_0")!),
        new ApiKey { Provider = ModelProviders.AppOllama, Label = string.Empty, Secret = string.Empty }, string.Empty);

    private static async Task<Profile> PrepareAsync(DesktopHarness h)
    {
        await FirstRunTests.CreateAsync(h);
        var profile = new Profile { Name = "Byzantium", ContentLanguage = ContentLanguages.English, ConceptText = "Byzantine history." };
        await h.Get<IProfileStore>().SaveAsync(profile, None);
        await h.Get<AppSession>().SelectProfileAsync(profile, None);
        await h.Get<IApiKeyStore>().SaveAsync(new ApiKey { Provider = "fal", Label = "k", Secret = "fake-test-key-video-4567" }, None);
        await h.Get<IApiKeyStore>().SaveAsync(new ApiKey { Provider = "anthropic", Label = "k", Secret = "fake-test-key-video-0123" }, None);
        await h.Get<IStepModelStore>().SaveAsync(new StepModelChoice { Step = PipelineSteps.Images, ProfileId = profile.Id, Provider = "fal", Model = "fal-ai/flux-2/flash" },
            None);
        return profile;
    }

    private static Task<VideoRecord> VideoAsync(DesktopHarness h, Profile profile, Concept concept, ResolvedModel model) =>
        h.Get<VideoPipeline>().RunAsync(new VideoJob(concept, profile, new VideoOptions { Engine = EngineKind.HyperFrames, Speech = false, Images = ImageSources.Model },
            model), null, None);

    [AvaloniaFact]
    public async Task ALocalModelWithoutScenesAndWithoutImagesStillGivesAVideoWithEveryScenePicture()
    {
        var engine = new FakeEngine();
        var images = new FakeImages();
        const string flat = """{"konseptler":[{"baslik":"Greek fire","ozet":"One. Two.","icerik":"In 672 the city was under siege, and a strange fire burned on the water."}]}""";
        const string scenes = """
            {"konseptler":[{"baslik":"Greek fire","ozet":"One. Two.","sahneler":[
              {"ekranda":"The walls under siege","seslendirme":"In 672 the city was under siege."},
              {"ekranda":"Arab ships in the strait","seslendirme":"A great fleet waited in the strait."},
              {"ekranda":"A siphon on a dromon","seslendirme":"Then the Romans brought out a new weapon."},
              {"ekranda":"Fire on the water","seslendirme":"A strange fire burned on the water."}]}]}
            """;
        var chat = new FakeChat().Answer(_ => Answer(flat)).Answer(_ => Answer(scenes)).Answer(_ => Answer(Prompts(4))).Answer(_ => Code(NoImages));
        using var h = Harness(chat, engine, images);
        var profile = await PrepareAsync(h);
        var media = (FakeMedia)h.Get<IMediaTools>();
        media.Pixels = (_, _) => Gradient();

        // The concept: no scenes at first, one repair with the findings.
        var run = await h.Get<ConceptGenerator>().GenerateAsync(new ConceptRequest(profile, new IdeaBrief(string.Empty, 1), Local()), null, null, None);
        chat.Requests[1].User.ShouldContain("1 scenes, at least 4 needed (write about 6 scenes); scene 1 has an empty \"ekranda\"");
        run.ScenesMissing.ShouldBe(0);
        var concept = run.Accepted.ShouldHaveSingleItem();
        ConceptSceneCheck.Check(concept.Content, profile.ContentLanguage, profile.TargetSeconds).ShouldBeEmpty();

        // The video: four numbered prompts, four images, and code that never shows them → the app's own animation, no repair call.
        var record = await VideoAsync(h, profile, concept, Local());

        chat.Requests.Count.ShouldBe(4, "concept, its repair, the image prompts, one code call");
        chat.Requests[2].System.ShouldBe(ImagePromptBuilder.System);
        chat.Requests[3].System.ShouldContain("Checklist before you answer");
        images.Prompts.ShouldBe(["picture 1", "picture 2", "picture 3", "picture 4"]);
        record.Warnings.ShouldContain(VideoWarnings.AppCode);
        record.Warnings.ShouldNotContain(VideoWarnings.FrameCheck, Case.Sensitive, "the app's animation shows the images: the frame check passes");
        record.Warnings.ShouldContain(VideoWarnings.NoThumbnail, Case.Sensitive, "the fake thumbnail finds no frame that is not black: a yellow warning");
        var folder = LibraryPaths.Resolve(h.Get<IAppPaths>().LibraryDirectory, record.Folder);
        File.ReadAllText(Path.Combine(folder, VideoFolders.Source, engine.CodeFile)).Trim().ShouldBe(((IVideoEngine)engine).FallbackCode);
        media.Samples.Count(s => s.File == "silent.mp4" && s.Region == FrameRegion.Whole).ShouldBe(1, "one frame check of the one render");
    }

    [AvaloniaFact]
    public async Task EmptyFramesRenderALocalModelsVideoAgainWithTheAppsAnimationAndStopAnOnlineOne()
    {
        var engine = new FakeEngine();
        var images = new FakeImages();
        var chat = new FakeChat().Answer(_ => Answer(Prompts(2))).Answer(_ => Code(engine.SampleCode));
        using var h = Harness(chat, engine, images);
        var profile = await PrepareAsync(h);
        var media = (FakeMedia)h.Get<IMediaTools>();
        var flatRenders = new HashSet<int> { 1 };
        media.Pixels = (file, _) => file == "silent.mp4" && flatRenders.Contains(engine.Renders) ? Flat() : Gradient();
        var first = new Concept { ProfileId = profile.Id, Title = "Greek fire", Summary = "s", Content = "[on screen: map]\nThe siege.\n[on screen: ships]\nThe fire.", Language = "en" };
        await h.Get<IConceptStore>().AddAsync([first], None);

        // A local model: its code shows the images, but the rendered frames are empty → rendered again with the app's own animation.
        var record = await VideoAsync(h, profile, first, Local());

        engine.Renders.ShouldBe(2);
        chat.Requests.Count.ShouldBe(2, "no repair call");
        record.Warnings.ShouldContain(VideoWarnings.FrameCheck);
        record.Warnings.ShouldContain(VideoWarnings.AppCode);

        // An online model: the same empty frames stop the video with the reason; nothing more is paid.
        var second = new Concept { ProfileId = profile.Id, Title = "Iron chain", Summary = "s", Content = "[on screen: chain]\nThe chain.\n[on screen: harbour]\nThe harbour.", Language = "en" };
        await h.Get<IConceptStore>().AddAsync([second], None);
        chat.Answer(_ => Answer(Prompts(2))).Answer(_ => Code(engine.SampleCode));
        flatRenders.Add(3);
        var online = new ResolvedModel(h.Get<IModelCatalog>().Find("anthropic", "claude-sonnet-5-5")!,
            new ApiKey { Provider = "anthropic", Label = "k", Secret = "fake-test-key-video-0123" }, string.Empty);

        var error = await Should.ThrowAsync<VideoException>(() => VideoAsync(h, profile, second, online));

        error.Code.ShouldBe("frames");
        error.Message.ShouldContain("scene 1 shows an empty frame");
        chat.Requests.Count.ShouldBe(4, "the image prompts and one code call; no paid repair");
        engine.Renders.ShouldBe(3);
    }
}
