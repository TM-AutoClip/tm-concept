using Avalonia.Headless.XUnit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Shouldly;
using TMConcept.Core.Chat;
using TMConcept.Core.Concepts;
using TMConcept.Core.Engines;
using TMConcept.Core.Keys;
using TMConcept.Core.Media;
using TMConcept.Core.Paths;
using TMConcept.Core.Profiles;
using TMConcept.Core.Tools;
using TMConcept.Core.Videos;
using TMConcept.Desktop.Startup;
using TMConcept.Desktop.ViewModels;
using TMConcept.Tests.Providers;

namespace TMConcept.Tests.Desktop;

public sealed class VideoPageTests
{
    private const string Content = "[on screen: a map]\nIn 672 the city was under siege.\n[on screen: burning ships]\nA strange fire burned on the water.";

    private static ChatResponse Code(string code) => new("```js\n" + code + "\n```", new ChatUsage(500, 0, 0, 800), null, "end_turn", "r");

    private static async Task<(VideoViewModel Page, Concept Concept)> PrepareAsync(DesktopHarness h)
    {
        await FirstRunTests.CreateAsync(h);
        var profile = new Profile { Name = "Byzantium", ContentLanguage = ContentLanguages.English, ConceptText = "x", SpeechEngine = string.Empty };
        await h.Get<IProfileStore>().SaveAsync(profile, CancellationToken.None);
        await h.Get<AppSession>().SelectProfileAsync(profile, CancellationToken.None);
        await h.Get<IApiKeyStore>().SaveAsync(new ApiKey { Provider = "anthropic", Label = "k", Secret = "fake-test-key-video-0123" }, CancellationToken.None);
        var concept = new Concept { ProfileId = profile.Id, Title = "Greek fire", Summary = "s", Content = Content, Language = "en", Status = ConceptStatus.Approved };
        await h.Get<IConceptStore>().AddAsync([concept], CancellationToken.None);
        var page = h.Get<VideoViewModel>();
        await page.ActivateAsync();
        page.CodeModel = page.Models.First(m => m.Model?.Id == "claude-sonnet-5-5");
        await page.EnqueueAsync(page.Approved.Single());
        return (page, concept);
    }

    private static DesktopHarness Harness(FakeChat chat, FakeEngine engine) => new(s =>
    {
        s.AddSingleton<IChatClientFactory>(chat);
        s.RemoveAll<IVideoEngine>();
        s.AddSingleton<IVideoEngine>(engine);
        s.AddSingleton<IMediaTools, FakeMedia>();
    });

    [AvaloniaFact]
    public async Task AQueuedConceptBecomesASilentVideoInTheLibrary()
    {
        var engine = new FakeEngine();
        using var h = Harness(new FakeChat().Answer(_ => Code(engine.SampleCode)), engine);
        var (page, concept) = await PrepareAsync(h);
        page.Queue.Count.ShouldBe(1);
        page.Approved.ShouldBeEmpty();

        await page.ProduceAllAsync();

        page.Queue.ShouldBeEmpty();
        page.Produced.Single().ShouldBe("Greek fire");
        var video = (await h.Get<IVideoStore>().ListAsync(concept.ProfileId, CancellationToken.None)).Single();
        video.Warnings.ShouldContain(VideoWarnings.SpeechOff);
        video.TotalUsd.ShouldBeGreaterThan(0);
        var folder = LibraryPaths.Resolve(h.Get<IAppPaths>().LibraryDirectory, video.Folder);
        File.Exists(Path.Combine(folder, "video.mp4")).ShouldBeTrue();
        File.ReadAllText(Path.Combine(folder, "subtitles.srt")).ShouldContain("siege");
        File.ReadAllText(Path.Combine(folder, "narration.txt")).ShouldContain("Scene 2");
        (await h.Get<IConceptStore>().GetAsync(concept.Id, CancellationToken.None))!.Status.ShouldBe(ConceptStatus.Produced);
        engine.Input!.Scenes.Count.ShouldBe(2);
        h.Get<TMConcept.Desktop.Logging.ActivityLog>().Entries.ShouldContain(e => e.IsWarning);
    }

    [AvaloniaFact]
    public async Task RejectedCodeGetsOneRepair()
    {
        var engine = new FakeEngine();
        var chat = new FakeChat().Answer(_ => Code("fetch('x')")).Answer(_ => Code(engine.SampleCode));
        using var h = Harness(chat, engine);
        var (page, concept) = await PrepareAsync(h);

        await page.ProduceAllAsync();

        chat.Requests.Count.ShouldBe(2);
        chat.Requests[1].User.ShouldContain("network request");
        (await h.Get<IVideoStore>().ListAsync(concept.ProfileId, CancellationToken.None)).Single().Warnings.ShouldContain(VideoWarnings.Repaired);
    }

    [AvaloniaFact]
    public async Task ARenderThatFailsTwiceStaysInTheQueueWithItsError()
    {
        var engine = new FakeEngine { FailRenders = 2 };
        using var h = Harness(new FakeChat().Answer(_ => Code(engine.SampleCode)).Answer(_ => Code(engine.SampleCode)), engine);
        var (page, concept) = await PrepareAsync(h);

        await page.ProduceAllAsync();

        page.Queue.Single().Failed.ShouldBeTrue();
        (await h.Get<IConceptStore>().GetAsync(concept.Id, CancellationToken.None))!.Status.ShouldBe(ConceptStatus.Queued);
        (await h.Get<IVideoStore>().ListAsync(concept.ProfileId, CancellationToken.None)).ShouldBeEmpty();
    }

    internal sealed class FakeEngine : IVideoEngine
    {
        public int FailRenders { get; set; }

        public TMConcept.Core.Render.RenderInput? Input { get; private set; }

        public EngineKind Kind => EngineKind.HyperFrames;

        public string Name => "Fake";

        public bool Experimental => false;

        public bool NeedsLicenseConsent => false;

        public string CodeFile => "scene.js";

        public string Contract => "contract";

        public string SampleCode => "window.__timelines = window.__timelines || {}; window.__timelines[\"concept\"] = gsap.timeline({ paused: true });";

        public EngineStatus Status() => new(true, []);

        public Task InstallAsync(bool agreed, IProgress<ToolProgress>? progress, CancellationToken cancellationToken) => Task.CompletedTask;

        public void WriteProject(EngineRenderRequest request)
        {
            var findings = EngineCodeCheck.Check(Kind, request.Code);
            if (findings.Count > 0)
            {
                throw new EngineException("rejected") { Code = "check", Findings = findings };
            }

            Input = request.Input;
            Directory.CreateDirectory(request.Folder);
        }

        public Task RenderAsync(EngineRenderRequest request, IProgress<EngineEvent>? progress, CancellationToken cancellationToken)
        {
            if (FailRenders-- > 0)
            {
                throw new EngineException("TypeError: x is undefined") { Code = "render" };
            }

            File.WriteAllBytes(request.Output, new byte[2048]);
            progress?.Report(new EngineEvent(1, "done", Done: true));
            return Task.CompletedTask;
        }
    }

    internal sealed class FakeMedia : IMediaTools
    {
        public Task MuxAsync(string folder, string silentVideo, IReadOnlyList<(string Wav, long StartMs)> clips, string? assFile, string output,
            CancellationToken cancellationToken)
        {
            File.Copy(silentVideo, output, overwrite: true);
            return Task.CompletedTask;
        }

        public Task<bool> ThumbnailAsync(string video, string jpeg, CancellationToken cancellationToken) => Task.FromResult(false);
    }
}
