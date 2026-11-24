using Avalonia.Headless.XUnit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Shouldly;
using TMConcept.Core.Components;
using TMConcept.Core.DiskSpace;
using TMConcept.Core.Engines;
using TMConcept.Core.Models;
using TMConcept.Core.Pipeline;
using TMConcept.Core.Profiles;
using TMConcept.Desktop.Dialogs;
using TMConcept.Desktop.DiskSpace;
using TMConcept.Desktop.Localization;
using TMConcept.Desktop.Os;
using TMConcept.Desktop.Startup;
using TMConcept.Desktop.ViewModels;

namespace TMConcept.Tests.Desktop;

/// <summary>
/// The storage manager (Settings → Storage, the developer, 17 Nov 2026): which components are used comes from every profile's saved step
/// choices (the catalog model's components, the video code step's engine, ffmpeg for the app itself) closed over the dependencies; "Clean
/// up unused" never takes a used one; neither clean-up takes a model (only ticking its row does); everything is deleted for good (0.1.28:
/// no recycle bin); a row that fails is reported and the others go on; nothing is touched while work runs. Fake components on a temporary data folder; nothing is downloaded.
/// </summary>
public sealed class StorageManagerTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    [AvaloniaFact]
    public async Task UsageComesFromEveryProfilesSavedChoicesClosedOverTheDependencies()
    {
        var setup = await SetupAsync();
        using var h = setup.H;

        var usage = await h.Get<IComponentUsage>().ComputeAsync(None);

        usage.Uses.Keys.Order(StringComparer.Ordinal).ShouldBe(
            ["engine.remotion", "ffmpeg", "image.z-image-turbo", "music-library", "music.ace-step-1.5", "node", "sd-cpp", "sherpa-onnx", "voice.kokoro",
                "whisper-cpp", "whisper-model.base"]);
        usage.For("music-library").ShouldBe([new DiskUse(string.Empty, string.Empty)], "templates choose the music library, so the app keeps it (0.1.45)");
        usage.For("music.ace-step-1.5").ShouldBe([new DiskUse(string.Empty, string.Empty)], "templates choose the local music model too (0.1.46)");
        usage.For("whisper-model.base").ShouldBe([new DiskUse(string.Empty, string.Empty)], "every video's subtitles use whisper.cpp once installed (0.1.48)");
        usage.For("voice.kokoro").ShouldBe([new DiskUse("Ottoman", PipelineSteps.Speech)]);
        usage.For("sherpa-onnx").ShouldBe([new DiskUse("Ottoman", PipelineSteps.Speech)], "a dependency is used by whoever uses the voice");
        usage.For("sd-cpp").ShouldBe([new DiskUse("Byzantium", PipelineSteps.Images)]);
        usage.For("ffmpeg").ShouldContain(u => u.IsApp, "the app itself needs ffmpeg");
        usage.For("ffmpeg").ShouldContain(new DiskUse("Ottoman", PipelineSteps.VideoCode), "and so does the chosen engine");
        usage.IsUsed("voice.supertonic").ShouldBeFalse("the voice the profile switched away from");
        usage.IsUsed("engine.hyperframes").ShouldBeFalse();
    }

    [Fact]
    public void TheEngineCodeOfAVideoCodeChoiceIsTheEngineComponentsCode()
    {
        using var h = new DesktopHarness();
        var usage = h.Get<IComponentUsage>();
        foreach (var kind in Enum.GetValues<EngineKind>())
        {
            var choice = new StepModelChoice
            {
                Step = PipelineSteps.VideoCode, OptionsJson = StepModelResolver.OptionsJson(new StepOptions { Engine = kind }),
            };
            usage.CodesOf(choice).ShouldBe([TMConcept.Engines.Components.EngineComponent.CodeOf(kind)]);
        }

        usage.CodesOf(new StepModelChoice { Step = PipelineSteps.Speech, Provider = ModelProviders.Off }).ShouldBeEmpty("a step that is off needs nothing");
        usage.CodesOf(new StepModelChoice { Step = PipelineSteps.Speech, Provider = ModelProviders.Local, Model = "kokoro" }).ShouldBe(["voice.kokoro"]);
    }

    [AvaloniaFact]
    public async Task TheSharedBrowserIsUsedWhileAProfileRendersWithHyperFramesOrRevideo()
    {
        // The browser is no dependency of an engine (the engine's install fetches it), but HyperFrames and Revideo render with it: clean-up
        // must keep it (0.1.18 before this fix sent tools/chrome to the bin, and the next video stopped for a 150 MB download).
        using var h = new DesktopHarness();
        await FirstRunTests.CreateAsync(h);
        var profile = new Profile { Name = "Byzantium", ContentLanguage = ContentLanguages.English, ConceptText = "x" };
        await h.Get<IProfileStore>().SaveAsync(profile, None);
        var chrome = h.Get<TMConcept.Engines.Engines.ChromeLocator>().Root;
        Put(chrome, 900);
        var steps = h.Get<IStepModelStore>();
        var usage = h.Get<IComponentUsage>();
        foreach (var (kind, used) in new[] { (EngineKind.HyperFrames, true), (EngineKind.Revideo, true), (EngineKind.Remotion, false) })
        {
            await steps.SaveAsync(new StepModelChoice
            {
                Step = PipelineSteps.VideoCode, ProfileId = profile.Id, Provider = "anthropic", Model = "claude-sonnet-5-5",
                OptionsJson = StepModelResolver.OptionsJson(new StepOptions { Engine = kind }),
            }, None);

            var map = await usage.ComputeAsync(None);
            map.IsUsed(ComponentCodes.Browser).ShouldBe(used, kind.ToString());
            if (used)
            {
                map.For(ComponentCodes.Browser).ShouldBe([new DiskUse("Byzantium", PipelineSteps.VideoCode)]);
                var report = await h.Get<IDiskCleaner>().ReportAsync(None);
                var browser = report.Items.Single(i => i.Key == "component:" + ComponentCodes.Browser);
                browser.UsedBy.ShouldNotBeEmpty();
                DiskSelection.Unused(report.Items).ShouldNotContain(browser, "clean-up never takes what a chosen engine renders with");
            }
        }
    }

    [AvaloniaFact]
    public async Task TheReportListsStatesSizesLeftoversAndTotals()
    {
        var setup = await SetupAsync();
        using var h = setup.H;

        var report = await h.Get<IDiskCleaner>().ReportAsync(None);

        State(report, "component:voice.kokoro").ShouldBe(DiskItemState.Used);
        State(report, "component:voice.supertonic").ShouldBe(DiskItemState.Unused);
        State(report, "component:engine.hyperframes").ShouldBe(DiskItemState.Unused);
        State(report, "component:image.flux2-klein-4b").ShouldBe(DiskItemState.Incomplete, "a half-finished download is listed so it can be removed");
        report.Items.ShouldNotContain(i => i.Key == "component:speech-python", "nothing on disk");
        report.Items.Single(i => i.Key == "component:voice.supertonic").Bytes.ShouldBe(5000);
        report.Items.Single(i => i.Key == "tools:unowned").Paths.ShouldBe([Path.Combine(h.Paths.ToolsDirectory, "ffmpeg", "6.0")],
            "an older version nothing owns; the pinned version and the marker file next to it stay");
        report.Items.ShouldContain(i => i.Key == "tools:downloads");
        report.Items.ShouldContain(i => i.Key.StartsWith("tools:aside:tools.old-", StringComparison.Ordinal));
        report.Items.Single(i => i.Key == "work:speech-cache:old").Bytes.ShouldBe(120);
        report.Items.Single(i => i.Key == "work:speech-cache:recent").Bytes.ShouldBe(300);
        report.Items.ShouldContain(i => i.Key == "work:videos:old");
        report.Items.ShouldContain(i => i.Key == "work:videos:recent");

        var totals = report.Totals.ToDictionary(t => t.Area, t => t.Bytes);
        totals[DiskArea.Models].ShouldBe(1000 + 5000 + 3000 + 700);
        totals[DiskArea.Work].ShouldBe(300 + 120 + 400 + 500);
        totals[DiskArea.Backups].ShouldBe(0);
        totals[DiskArea.Tools].ShouldBeGreaterThan(0);
    }

    [AvaloniaFact]
    public async Task CleanUpNeverTakesAUsedComponentAndKeepsRecentWorkForARetry()
    {
        var setup = await SetupAsync();
        using var h = setup.H;
        var report = await h.Get<IDiskCleaner>().ReportAsync(None);

        var unused = DiskSelection.Unused(report.Items).Select(i => i.Key).ToList();

        unused.ShouldNotContain("component:voice.supertonic", "0.1.28: an unused model goes only when its row is ticked");
        unused.ShouldContain("component:engine.hyperframes");
        unused.ShouldNotContain("component:image.flux2-klein-4b", "an image model, even a half-finished one, only when ticked");
        unused.ShouldContain("tools:downloads");
        unused.ShouldContain("tools:unowned");
        unused.ShouldContain("work:speech-cache:old");
        unused.ShouldNotContain("work:speech-cache:recent", "a retry speaks from it instead of paying an online voice again");
        unused.ShouldContain("work:videos:old");
        unused.ShouldNotContain("work:videos:recent", "a retry still uses it");
        foreach (var used in report.Items.Where(i => i.State == DiskItemState.Used || i.UsedBy.Count > 0))
        {
            unused.ShouldNotContain(used.Key);
        }

        DiskSelection.Full(report.Items).Select(i => i.Key).ShouldContain("component:engine.remotion", "the full delete takes every tool");
        DiskSelection.Full(report.Items).Where(i => i.Area == DiskArea.Models).ShouldBeEmpty("and never a model");
        DiskSelection.Full(report.Items).Select(i => i.Key).ShouldContain("work:videos:recent");
        DiskSelection.Full(report.Items).Select(i => i.Key).ShouldContain("work:speech-cache:recent");
    }

    [AvaloniaFact]
    public async Task AnImageModelFolderThisCopyDoesNotKnowIsNeverTakenByTheCleanUp()
    {
        var setup = await SetupAsync();
        using var h = setup.H;
        // Installed by a newer version or a remote catalog entry this copy refused: gigabytes, not a leftover.
        var unknown = Path.Combine(h.Paths.ToolsDirectory, "images", "remote-only-model");
        Put(unknown, 900, "model.gguf");

        var report = await h.Get<IDiskCleaner>().ReportAsync(None);

        report.Items.Single(i => i.Key == "tools:unknown-image-models").Paths.ShouldBe([unknown]);
        report.Items.Single(i => i.Key == "tools:unowned").Paths.ShouldNotContain(unknown);
        DiskSelection.Unused(report.Items).Select(i => i.Key).ShouldNotContain("tools:unknown-image-models");
        DiskSelection.Full(report.Items).Select(i => i.Key).ShouldNotContain("tools:unknown-image-models", "a model goes only when ticked");
    }

    [AvaloniaFact]
    public async Task RemovedItemsAreDeletedForGoodAndTheInstallRecordForgetsThem()
    {
        var setup = await SetupAsync();
        using var h = setup.H;
        var cleaner = h.Get<IDiskCleaner>();
        var report = await cleaner.ReportAsync(None);
        h.Get<IInstallRecord>().Installed("voice.supertonic");

        var ticked = DiskSelection.Unused(report.Items).Append(report.Items.Single(i => i.Key == "component:voice.supertonic")).ToList();
        var outcome = await cleaner.RemoveAsync(ticked, None);

        outcome.Busy.ShouldBeFalse();
        outcome.Failed.ShouldBeEmpty();
        setup.Discard.Discarded.ShouldContain(setup.Supertonic.Folder!);
        setup.Discard.Discarded.ShouldContain(Path.Combine(h.Paths.ToolsDirectory, "ffmpeg", "6.0"));
        Directory.Exists(setup.Supertonic.Folder).ShouldBeFalse();
        Directory.Exists(setup.Kokoro.Folder).ShouldBeTrue("a used voice is never cleaned up");
        File.Exists(Path.Combine(h.Paths.ToolsDirectory, "ffmpeg", "tool.fallback")).ShouldBeTrue("files next to versions stay");
        Directory.Exists(Path.Combine(h.Paths.WorkDirectory, "videos", "recent")).ShouldBeTrue();
        h.Get<IInstallRecord>().List().ShouldNotContain(r => r.Code == "voice.supertonic");
        setup.Confirm.Asked.ShouldBeEmpty("the page asked once before; the cleaner asks nothing more");
        outcome.Freed.ShouldBeGreaterThan(5000);
    }

    [AvaloniaFact]
    public async Task ARowThatCannotBeDeletedIsReportedWithItsReasonAndTheOthersGo()
    {
        var setup = await SetupAsync();
        using var h = setup.H;
        var cleaner = h.Get<IDiskCleaner>();
        setup.Discard.FailWhen = p => p == setup.Supertonic.Folder;
        var report = await cleaner.ReportAsync(None);
        var supertonic = report.Items.Single(i => i.Key == "component:voice.supertonic");
        var downloads = report.Items.Single(i => i.Key == "tools:downloads");

        var outcome = await cleaner.RemoveAsync([supertonic, downloads], None);

        outcome.Failed.ShouldHaveSingleItem().Item.ShouldBe(supertonic);
        outcome.Failed[0].Reason.ShouldContain("being used by another process");
        outcome.Removed.ShouldBe([downloads]);
        Directory.Exists(setup.Supertonic.Folder).ShouldBeTrue();
        setup.Confirm.Asked.ShouldBeEmpty("no second question: the failure is reported");

        setup.Discard.FailWhen = _ => false;
        (await cleaner.RemoveAsync([supertonic], None)).Removed.ShouldBe([supertonic], "a second try finishes it");
        Directory.Exists(setup.Supertonic.Folder).ShouldBeFalse();
    }

    [AvaloniaFact]
    public async Task NothingIsRemovedWhileWorkRuns()
    {
        var setup = await SetupAsync();
        using var h = setup.H;
        var cleaner = h.Get<IDiskCleaner>();
        var report = await cleaner.ReportAsync(None);
        h.Get<AppSession>().WorkRunning = true;

        var outcome = await cleaner.RemoveAsync(DiskSelection.Full(report.Items), None);

        outcome.Busy.ShouldBeTrue();
        setup.Discard.Discarded.ShouldBeEmpty();
        Directory.Exists(setup.Supertonic.Folder).ShouldBeTrue();
    }

    [AvaloniaFact]
    public async Task NothingIsRemovedWhileAnEngineRendersItsSample()
    {
        // Settings → "Render sample" runs Node.js and the browser from the components folder; it is work like a video.
        var setup = await SetupAsync();
        using var h = setup.H;
        var cleaner = h.Get<IDiskCleaner>();
        var report = await cleaner.ReportAsync(None);
        var sample = h.Get<TMConcept.Engines.Engines.SampleRender>();
        var engine = new HeldEngine();
        var rendering = sample.RenderAsync(engine, null, None);
        await engine.Started.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        sample.Busy.ShouldBeTrue();
        (await cleaner.RemoveAsync(DiskSelection.Full(report.Items), None)).Busy.ShouldBeTrue();
        setup.Discard.Discarded.ShouldBeEmpty();

        engine.Finish.SetResult();
        await rendering;
        sample.Busy.ShouldBeFalse();
        (await cleaner.RemoveAsync([report.Items.Single(i => i.Key == "component:voice.supertonic")], None)).Busy.ShouldBeFalse();
    }

    /// <summary>An engine whose render waits until the test lets it finish.</summary>
    private sealed class HeldEngine : IVideoEngine
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Finish { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public EngineKind Kind => EngineKind.HyperFrames;

        public string Name => "Held";

        public bool Experimental => false;

        public bool NeedsLicenseConsent => false;

        public string CodeFile => "scene.js";

        public string Contract => "contract";

        public string SampleCode => "sample";

        public EngineStatus Status() => new(true, []);

        public Task InstallAsync(bool agreed, IProgress<TMConcept.Core.Tools.ToolProgress>? progress, CancellationToken cancellationToken) => Task.CompletedTask;

        public void WriteProject(EngineRenderRequest request) => Directory.CreateDirectory(request.Folder);

        public async Task RenderAsync(EngineRenderRequest request, IProgress<EngineEvent>? progress, CancellationToken cancellationToken)
        {
            Started.SetResult();
            await Finish.Task.WaitAsync(cancellationToken);
        }
    }

    [Fact]
    public async Task AFolderOutsideTheDataFolderIsNeverDiscarded()
    {
        using var h = new DesktopHarness();
        var outside = Path.Combine(TestFolders.Root, "outside-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outside);
        try
        {
            await Should.ThrowAsync<InvalidOperationException>(() => h.Get<PermanentDiscard>().DiscardAsync(outside, None));
            await Should.ThrowAsync<InvalidOperationException>(() => h.Get<PermanentDiscard>().DiscardAsync(h.Paths.DataDirectory, None));
            Directory.Exists(outside).ShouldBeTrue();
            FolderDiscardBase.Inside(Path.Combine(h.Paths.DataDirectory, "tools", "x"), h.Paths.DataDirectory).ShouldBeTrue();
            FolderDiscardBase.Inside(h.Paths.DataDirectory + "-other", h.Paths.DataDirectory).ShouldBeFalse("a name that only starts the same");
        }
        finally
        {
            Directory.Delete(outside, recursive: true);
        }
    }

    [AvaloniaFact]
    public async Task TheStoragePageShowsWhoUsesWhatAndCleansUpAfterAConfirmation()
    {
        var setup = await SetupAsync();
        using var h = setup.H;
        h.Get<Localizer>().Use("tr");
        var page = h.Get<StorageSettingsViewModel>();

        await page.ActivateAsync();

        var kokoro = page.Items.Single(r => r.Item.Key == "component:voice.kokoro");
        kokoro.State.ShouldBe("kullanılıyor");
        kokoro.UsedBy.ShouldBe("Kullanan: Ottoman / " + h.Get<Localizer>()["step.speech"]);
        page.Items.Single(r => r.Item.Key == "component:voice.supertonic").State.ShouldBe("kullanılmıyor");
        page.Totals.Select(t => t.Label).ShouldContain("Modeller");

        await page.CleanUnusedAsync();

        setup.Confirm.Asked.ShouldBe([h.Get<Localizer>()["storage.clean.title"]]);
        setup.Confirm.Messages[0].ShouldNotContain("Kokoro", Case.Insensitive, "a used one is never offered for clean-up");
        setup.Confirm.Messages[0].ShouldContain("Bu işlem geri alınamaz.");
        Directory.Exists(Path.Combine(h.Paths.ToolsDirectory, "engines", "hyperframes")).ShouldBeFalse("an unused tool is cleaned up");
        Directory.Exists(setup.Supertonic.Folder).ShouldBeTrue("an unused model stays unless ticked");
        Directory.Exists(setup.Kokoro.Folder).ShouldBeTrue();
        page.Items.ShouldNotContain(r => r.Item.Key == "tools:downloads", "the list is read again after the removal");
        page.Status.ShouldContain("kaldırıldı");

        // Remove selected: only the ticked rows.
        page.Items.Single(r => r.Item.Key == "component:voice.kokoro").IsSelected = true;
        await page.RemoveSelectedAsync();
        Directory.Exists(setup.Kokoro.Folder).ShouldBeFalse();
        Directory.Exists(setup.Remotion.Folder).ShouldBeTrue();
    }

    private static DiskItemState State(DiskReport report, string key) => report.Items.Single(i => i.Key == key).State;

    /// <summary>
    /// Two profiles: "Ottoman" narrates with Kokoro and makes video code with Remotion (an online code model); "Byzantium" draws with the
    /// local Z-Image Turbo. On disk: Kokoro, Supertonic (switched away from), HyperFrames (not chosen), Remotion, Node, ffmpeg (pinned 7.1 and
    /// an old 6.0), sherpa-onnx, sd-cpp, Z-Image Turbo, a half-finished FLUX.2 download, leftovers and work caches.
    /// </summary>
    private static async Task<Setup> SetupAsync()
    {
        var folder = Path.Combine(TestFolders.Root, "storage-" + Guid.NewGuid().ToString("N"));
        var tools = Path.Combine(folder, "tools");
        FakeComponent Make(string code, bool ready, string relative, int bytes, params string[] dependsOn)
        {
            var component = new FakeComponent(code, ready, dependsOn) { Folder = Path.Combine(tools, relative) };
            Put(component.Folder, bytes);
            return component;
        }

        var kokoro = Make("voice.kokoro", true, Path.Combine("speech", "kokoro"), 1000, "sherpa-onnx");
        var supertonic = Make("voice.supertonic", true, Path.Combine("speech", "supertonic"), 5000, "sherpa-onnx");
        var remotion = Make("engine.remotion", true, Path.Combine("engines", "remotion"), 2000, "node", "ffmpeg");
        var hyperframes = Make("engine.hyperframes", true, Path.Combine("engines", "hyperframes"), 2500, "node", "ffmpeg");
        var image = Make("image.z-image-turbo", true, Path.Combine("images", "z-image-turbo"), 3000, "sd-cpp");
        var flux = new FakeComponent("image.flux2-klein-4b", false, "sd-cpp") { Folder = Path.Combine(tools, "images", "flux2-klein-4b") };
        Put(flux.Folder, 700, "model.part");
        var python = new FakeComponent("speech-python", false) { Folder = Path.Combine(tools, "speech-py", "python") };
        FakeComponent[] all =
        [
            kokoro, supertonic, remotion, hyperframes, image, flux, python,
            Make("sherpa-onnx", true, Path.Combine("sherpa-onnx", "1.12"), 100),
            Make("node", true, Path.Combine("node", "22"), 100),
            Make("ffmpeg", true, Path.Combine("ffmpeg", "7.1"), 100),
            Make("sd-cpp", true, Path.Combine("sd-cpp-vulkan", "1106"), 100),
        ];

        // Leftovers: an old ffmpeg version, the marker next to the versions, an unfinished download, an old components folder, caches.
        Put(Path.Combine(tools, "ffmpeg", "6.0"), 50);
        File.WriteAllText(Path.Combine(tools, "ffmpeg", "tool.fallback"), "x");
        Put(Path.Combine(tools, "downloads"), 60, "ffmpeg.zip.part");
        Put(Path.Combine(tools, "speech-py", "scripts"), 70);
        Put(Path.Combine(folder, "tools.old-20261101-120000"), 80);
        Put(Path.Combine(folder, "work", "speech-cache"), 300);
        Put(Path.Combine(folder, "work", "speech-cache"), 120, "old-clip.wav");
        File.SetLastWriteTimeUtc(Path.Combine(folder, "work", "speech-cache", "old-clip.wav"), DateTime.UtcNow.AddDays(-10));
        var old = Path.Combine(folder, "work", "videos", "old");
        Put(old, 400);
        Directory.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddDays(-10));
        Put(Path.Combine(folder, "work", "videos", "recent"), 500);

        var discard = new RecordingDiscard();
        var confirm = new FakeConfirm { Answer = true };
        var h = new DesktopHarness(folder, s =>
        {
            s.RemoveAll<IComponentSource>();
            s.AddSingleton<IComponentSource>(new FakeComponents(all));
            discard.Register(s);
            s.AddSingleton<IConfirmDialog>(confirm);
        });
        await FirstRunTests.CreateAsync(h);
        var steps = h.Get<IStepModelStore>();
        var ottoman = new Profile { Name = "Ottoman", ContentLanguage = ContentLanguages.English, ConceptText = "x" };
        var byzantium = new Profile { Name = "Byzantium", ContentLanguage = ContentLanguages.English, ConceptText = "x" };
        await h.Get<IProfileStore>().SaveAsync(ottoman, None);
        await h.Get<IProfileStore>().SaveAsync(byzantium, None);
        await steps.SaveAsync(new StepModelChoice { Step = PipelineSteps.Speech, ProfileId = ottoman.Id, Provider = ModelProviders.Local, Model = "kokoro" }, None);
        await steps.SaveAsync(new StepModelChoice
        {
            Step = PipelineSteps.VideoCode, ProfileId = ottoman.Id, Provider = "anthropic", Model = "claude-sonnet-5-5",
            OptionsJson = StepModelResolver.OptionsJson(new StepOptions { Engine = EngineKind.Remotion }),
        }, None);
        await steps.SaveAsync(new StepModelChoice { Step = PipelineSteps.Images, ProfileId = byzantium.Id, Provider = ModelProviders.Local, Model = "z-image-turbo" },
            None);
        await steps.SaveAsync(new StepModelChoice { Step = PipelineSteps.Embedding, ProfileId = byzantium.Id, Provider = ModelProviders.Off }, None);
        return new Setup(h, discard, confirm, kokoro, supertonic, remotion);
    }

    private static void Put(string folder, int bytes, string name = "data.bin")
    {
        Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder, name), new byte[bytes]);
    }

    private sealed record Setup(DesktopHarness H, RecordingDiscard Discard, FakeConfirm Confirm, FakeComponent Kokoro, FakeComponent Supertonic, FakeComponent Remotion);
}
