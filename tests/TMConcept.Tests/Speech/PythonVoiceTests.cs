using Avalonia.Headless.XUnit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using TMConcept.Core.Components;
using TMConcept.Core.Hardware;
using TMConcept.Core.Models;
using TMConcept.Core.Pipeline;
using TMConcept.Core.Profiles;
using TMConcept.Core.Speech;
using TMConcept.Desktop.Dialogs;
using TMConcept.Desktop.Localization;
using TMConcept.Desktop.Startup;
using TMConcept.Desktop.ViewModels;
using TMConcept.Providers.Catalog;
using TMConcept.Speech.Components;
using TMConcept.Speech.Python;
using TMConcept.Tests.Desktop;
using TMConcept.Tests.Storage;

namespace TMConcept.Tests.Speech;

/// <summary>
/// The Python voices as the app offers them: catalog entries with their licences and graphics card classes, pinned model files, the
/// components and their large downloads, where a voice runs (NVIDIA card or processor), the worker's start line and variables, and the
/// step's licence question and memory note (fake components and a fake card; nothing is downloaded or run).
/// </summary>
public sealed class PythonVoiceTests
{
    private const long GiB = 1024L * 1024 * 1024;

    private static readonly GpuReport Nvidia12 = new([new GpuAdapter(GpuVendor.Nvidia, "RTX 3060", 12 * GiB)], true, true);

    private static readonly GpuReport Nvidia6 = new([new GpuAdapter(GpuVendor.Nvidia, "RTX 2060", 6 * GiB)], true, true);

    private static readonly GpuReport Amd16 = new([new GpuAdapter(GpuVendor.Amd, "Radeon RX 9070", (long)(15.8 * GiB))], false, true);

    [Fact]
    public void EveryPythonVoiceIsInTheCatalogWithItsLicenceAndCardClass()
    {
        var catalog = new EmbeddedModelCatalog();
        foreach (var voice in PythonVoices.All)
        {
            var model = catalog.Find(ModelProviders.Local, voice.Code).ShouldNotBeNull(voice.Code);
            model.Capabilities.ShouldBe(ModelCapability.Speech);
            model.Requires.ShouldBe([ComponentCodes.VoicePrefix + voice.Code]);
            model.License.ShouldNotBeNullOrEmpty();
            (model.MinVideoMemoryGb > 0).ShouldBe(voice.OnCard, voice.Code + ": a card class only for a voice that uses the card");
        }

        string[] nonCommercial = [SpeechEngines.XttsV2, SpeechEngines.F5Turkish, SpeechEngines.MmsTurkish];
        catalog.With(ModelCapability.Speech).Where(m => m.LicenseUse == LicenseUse.NonCommercial).Select(m => m.Id).ShouldBe(nonCommercial, ignoreOrder: true);
        catalog.Find(ModelProviders.Local, SpeechEngines.Trendyol)!.License.ShouldBe("mit");
        catalog.Find(ModelProviders.Local, SpeechEngines.Ahenk)!.License.ShouldBe("cc-by-4.0");
        catalog.Find(ModelProviders.Local, SpeechEngines.VoxCpm2)!.MinVideoMemoryGb.ShouldBe(8);
    }

    [Fact]
    public void ModelFilesArePinnedToACommitWithTheirHashes()
    {
        foreach (var voice in PythonVoices.All)
        {
            voice.Files.ShouldNotBeEmpty();
            voice.Files.Select(f => f.ExecutablePath).ShouldBeUnique();
            foreach (var file in voice.Files)
            {
                file.Url.Host.ShouldBe("huggingface.co");
                file.Url.AbsolutePath.ShouldMatch("^/[^/]+/[^/]+/resolve/[0-9a-f]{40}/", voice.Code + ": a commit, never a branch");
                file.Sha256.ShouldMatch("^[0-9a-f]{64}$");
                file.ApproximateBytes.ShouldBeGreaterThan(0);
            }
        }

        PythonVoices.F5Turkish.Files.Select(f => f.ExecutablePath).ShouldContain("vocos/pytorch_model.bin", "the vocoder is downloaded with the voice, not at run time");
        PythonVoices.Ahenk.BaseVoice.ShouldBe(SpeechEngines.VoxCpm2);
        Should.Throw<ArgumentException>(() => PythonModelFiles.Pinned("a/b", "main", "x", new string('0', 64), 1));
    }

    [Fact]
    public async Task AVoiceNeedsUvPythonItsPackagesAndItsModelInThatOrder()
    {
        using var paths = new TempPaths();
        var gpu = new FakeGpu(Nvidia12);
        var layout = new PythonLayout(paths);
        var installer = new PythonInstaller(layout, null!, null!, gpu, NullLogger<PythonInstaller>.Instance);
        var models = new PythonModelStore(layout, null!, NullLogger<PythonModelStore>.Instance);
        var engines = PythonVoices.All.Select(v => PythonWorkerTests.Engine(paths, null!, gpu, v)).ToList();
        var source = new PythonComponentSource(engines, new EmbeddedModelCatalog(), installer, layout, models);
        var planner = new ComponentPlanner([source, new FakeComponents(new FakeComponent(ComponentCodes.Uv, ready: false))], NullLogger<ComponentPlanner>.Instance);

        var plan = await planner.PlanAsync([ComponentCodes.VoicePrefix + SpeechEngines.Ahenk], CancellationToken.None);
        plan.Missing.Select(c => c.Code).ShouldBe([ComponentCodes.Uv, ComponentCodes.Python, "speech-env.voxcpm", "voice.voxcpm2", "voice.ahenk"]);
        var environment = plan.Downloads.Single(d => d.Name.StartsWith("VoxCPM2 Python packages", StringComparison.Ordinal));
        environment.Name.ShouldContain("CUDA");
        environment.ApproximateBytes.ShouldBeGreaterThan(3 * GiB, "the confirmation must show that PyTorch with CUDA is several GB");
        plan.Downloads.Single(d => d.Name.StartsWith("VoxCPM2 model", StringComparison.Ordinal)).ApproximateBytes.ShouldBeGreaterThan(4 * GiB);

        var xtts = (await planner.PlanAsync([ComponentCodes.VoicePrefix + SpeechEngines.XttsV2], CancellationToken.None)).Downloads;
        xtts.Single(d => d.Name.StartsWith("XTTS-v2 model", StringComparison.Ordinal)).Name.ShouldContain("NON-COMMERCIAL");
        source.Components.Count(c => c.Code.StartsWith(ComponentCodes.PythonEnvironmentPrefix, StringComparison.Ordinal)).ShouldBe(4, "VoxCPM2, Trendyol and Ahenk share one");

        // Without an NVIDIA card the processor build is planned: smaller, and said so.
        var amd = new PythonInstaller(layout, null!, null!, new FakeGpu(Amd16), NullLogger<PythonInstaller>.Instance);
        var onAmd = new PythonEnvironmentComponent(PythonEnvironments.Xtts, amd, layout).Downloads().Single();
        onAmd.Name.ShouldContain("processor");
        onAmd.ApproximateBytes.ShouldBeLessThan(1 * GiB);
    }

    [Fact]
    public void AVoiceWhoseDownloadOnlyLeftAPartIsNotCountedAsInstalled()
    {
        using var paths = new TempPaths();
        var gpu = new FakeGpu(Nvidia12);
        var layout = new PythonLayout(paths);
        var models = new PythonModelStore(layout, null!, NullLogger<PythonModelStore>.Instance);
        IComponent component = new PythonVoiceComponent(PythonWorkerTests.Engine(paths, null!, gpu, PythonVoices.XttsV2), models, "CPML", nonCommercial: true);
        var folder = models.Folder(PythonVoices.XttsV2);

        component.WasInstalled.ShouldBeFalse();
        Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder, "model.pth.part"), [1, 2, 3]);
        component.WasInstalled.ShouldBeFalse("a cancelled or failed download leaves the folder with only a .part file (0.1.16 recorded it as installed)");

        File.WriteAllBytes(Path.Combine(folder, "config.json"), [1]);
        component.WasInstalled.ShouldBeTrue("a finished file of an earlier install counts, so a new version offers to update it");
    }

    [Fact]
    public void AVoiceRunsOnAnNvidiaCardOrOnTheProcessor()
    {
        using var paths = new TempPaths();
        PythonWorkerTests.Engine(paths, null!, new FakeGpu(Nvidia12), PythonVoices.XttsV2).Device.ShouldBe(ModelDevice.Gpu);
        PythonWorkerTests.Engine(paths, null!, new FakeGpu(Amd16), PythonVoices.XttsV2).Device.ShouldBe(ModelDevice.Cpu, "no PyTorch for AMD here");
        PythonWorkerTests.Engine(paths, null!, new FakeGpu(GpuReport.None), PythonVoices.VoxCpm2).Device.ShouldBe(ModelDevice.Cpu);
        PythonWorkerTests.Engine(paths, null!, new FakeGpu(Nvidia12), PythonVoices.MmsTurkish).Device.ShouldBe(ModelDevice.Cpu, "MMS stays on the processor");

        var model = new PythonLocalModel(new CatalogModel { Provider = ModelProviders.Local, Id = SpeechEngines.VoxCpm2, MinVideoMemoryGb = 8, MemoryGb = 12 },
            PythonWorkerTests.Engine(paths, null!, new FakeGpu(Nvidia12), PythonVoices.VoxCpm2));
        model.Tier.ShouldBe(VramTier.Gb8);
        model.Needs.Single().MemoryBytes.ShouldBe(12 * GiB);

        // PyTorch keeps every weight on the card: 7 of the 8 GB class free runs on the card (no false "main memory, slower"), 5 is refused.
        var card = new VramReading(16 * GiB, 7 * GiB, true, "PDH");
        var plan = model.PlanMemory(new MemorySnapshot(card, 32 * GiB), null);
        (plan.Placement, plan.Slower).ShouldBe((MemoryPlacement.Card, false));
        model.PlanMemory(new MemorySnapshot(card with { FreeBytes = 5 * GiB }, 32 * GiB), null).Placement.ShouldBe(MemoryPlacement.Refused);
    }

    [Fact]
    public void TheWorkerStartsOfflineWithTheVoicesOwnPythonAndModel()
    {
        using var paths = new TempPaths();
        var layout = new PythonLayout(paths);
        var ahenk = PythonWorkerTests.Engine(paths, null!, new FakeGpu(Nvidia12), PythonVoices.Ahenk);

        var start = ahenk.Start(ModelDevice.Gpu);
        start.Key.ShouldBe("ahenk|cuda");
        start.Program.ShouldBe(layout.EnvironmentPython("voxcpm"));
        start.Arguments.ShouldBe(["-I", "-u", Path.Combine(layout.ScriptsFolder, "worker.py")], "isolated: no user site, no PYTHONPATH, no script folder planting");
        start.Load.Adapter.ShouldBe("voxcpm");
        start.Load.Device.ShouldBe("cuda");
        start.Load.NeedMb.ShouldBe(8_192);
        start.Load.Model.ShouldBe(layout.ModelFolder("ahenk"));
        start.Load.Options!["kind"].ShouldBe("ahenk");
        start.Load.Options["base"].ShouldBe(layout.ModelFolder("voxcpm2"), "Ahenk's LoRA is put on the VoxCPM2 folder");
        start.Environment["HF_HUB_OFFLINE"].ShouldBe("1");
        start.Environment["HF_HOME"].ShouldStartWith(layout.Root);
        start.Environment.ShouldNotContainKey("COQUI_TOS_AGREED");
        start.Environment.ShouldNotContainKey("CUDA_VISIBLE_DEVICES");

        var cpu = ahenk.Start(ModelDevice.Cpu);
        cpu.Key.ShouldBe("ahenk|cpu");
        cpu.Load.NeedMb.ShouldBe(0);
        cpu.Environment["CUDA_VISIBLE_DEVICES"].ShouldBeEmpty("on the processor the card is not touched");
        PythonSpeechEngine.WorkerEnvironment(PythonVoices.XttsV2, layout, true, 2)["COQUI_TOS_AGREED"].ShouldBe("1", "accepted in the app before the download");
        PythonSpeechEngine.WorkerEnvironment(PythonVoices.XttsV2, layout, true, 2)["OMP_NUM_THREADS"].ShouldBe("2");

        var environment = new Dictionary<string, string?> { ["PATH"] = "p", ["OPENAI_API_KEY"] = "secret", ["HF_TOKEN"] = "secret" };
        PythonWorkerClient.Clean(environment, start.Environment);
        environment.ShouldNotContainKey("OPENAI_API_KEY");
        environment.ShouldNotContainKey("HF_TOKEN");
        environment["PATH"].ShouldBe("p");
    }

    [Fact]
    public void VoicesThatCopyAVoiceOfferTheUsersSamplesBesideTheirOwn()
    {
        using var paths = new TempPaths();
        var gpu = new FakeGpu(Nvidia12);
        PythonWorkerTests.Engine(paths, null!, gpu, PythonVoices.XttsV2).Voices("tr").Select(v => v.Id).ShouldContain("Ana Florence");
        PythonWorkerTests.Engine(paths, null!, gpu, PythonVoices.Ahenk).Voices("tr").Select(v => v.Id).ShouldBe(
            ["neutral", "calm", "cheerful", "empathetic", "sad", "angry", "surprised"]);
        PythonWorkerTests.Engine(paths, null!, gpu, PythonVoices.Trendyol).Supports("en").ShouldBeFalse();
        PythonWorkerTests.Engine(paths, null!, gpu, PythonVoices.VoxCpm2).Supports("id").ShouldBeTrue();
        PythonWorkerTests.Engine(paths, null!, gpu, PythonVoices.XttsV2).Ready.ShouldBeFalse("nothing is installed");
    }

    [AvaloniaFact]
    public async Task ANonCommercialVoiceAsksForItsLicenceBeforeAnyDownloadAndKeepsTheAnswer()
    {
        var voice = new FakeComponent(ComponentCodes.VoicePrefix + SpeechEngines.XttsV2, ready: false);
        var confirm = new FakeConfirm();
        using var h = new DesktopHarness(s =>
        {
            s.AddSingleton<IConfirmDialog>(confirm);
            s.AddSingleton<IGpuInfo>(new FakeGpu(Nvidia12));
            s.RemoveAll<IComponentSource>();
            s.AddSingleton<IComponentSource>(new FakeComponents(voice));
        });
        await FirstRunTests.CreateAsync(h);
        var profile = new Profile { Name = "Tr", ContentLanguage = ContentLanguages.Turkish, ConceptText = "x" };
        await h.Get<IProfileStore>().SaveAsync(profile, CancellationToken.None);
        var picker = h.Get<StepPickerViewModel>();
        await picker.LoadAsync(profile.Id, ContentLanguages.Turkish, PipelineSteps.VideoPage);
        var speech = picker.Row(PipelineSteps.Speech)!;
        speech.Selected = speech.Options.First(o => o.Model?.Id == SpeechEngines.XttsV2);
        var text = h.Get<Localizer>();

        confirm.Answers.Enqueue(false);
        (await picker.PrepareAsync([PipelineSteps.Speech], "install.purpose.video", CancellationToken.None)).ShouldBeFalse();
        confirm.Asked.ShouldBe([text.Format("models.license.title", "XTTS-v2 (non-commercial; your own voice)")]);
        confirm.Messages[0].ShouldContain(text["license.cpml"]);
        voice.Installs.ShouldBe(0, "nothing is downloaded before the licence is accepted");

        (await picker.PrepareAsync([PipelineSteps.Speech], "install.purpose.video", CancellationToken.None)).ShouldBeTrue();
        voice.Installs.ShouldBe(1);
        h.Get<AppSession>().Settings.AcceptedModelLicenses.ShouldContain("local/xtts-v2");

        var asked = confirm.Asked.Count;
        (await picker.PrepareAsync([PipelineSteps.Speech], "install.purpose.video", CancellationToken.None)).ShouldBeTrue();
        confirm.Asked.Count.ShouldBe(asked, "an accepted licence is not asked again");
    }

    [AvaloniaFact]
    public async Task TheStepSaysWhereAPythonVoiceRuns()
    {
        foreach (var (report, warning, error, ok) in new[] { (Amd16, true, false, false), (Nvidia6, false, true, false), (Nvidia12, false, false, true) })
        {
            using var h = new DesktopHarness(s =>
            {
                s.AddSingleton<IGpuInfo>(new FakeGpu(report));
                s.RemoveAll<IComponentSource>();
                s.AddSingleton<IComponentSource>(new FakeComponents());
            });
            await FirstRunTests.CreateAsync(h);
            var profile = new Profile { Name = "Tr", ContentLanguage = ContentLanguages.Turkish, ConceptText = "x" };
            await h.Get<IProfileStore>().SaveAsync(profile, CancellationToken.None);
            var picker = h.Get<StepPickerViewModel>();
            await picker.LoadAsync(profile.Id, ContentLanguages.Turkish, PipelineSteps.VideoPage);
            var speech = picker.Row(PipelineSteps.Speech)!;
            var text = h.Get<Localizer>();

            // The list itself shows the same before anything is picked (0.1.17: a card sees its own class and lower; others red).
            var entry = speech.Options.First(o => o.Model?.Id == SpeechEngines.VoxCpm2);
            (entry.IsWarning, entry.IsError, entry.IsOk).ShouldBe((warning, error, ok), "list: " + report.Adapters[0].Name);
            if (warning)
            {
                entry.Label.ShouldContain(text["models.tier.processor"]);
            }

            speech.Selected = entry;
            var clock = System.Diagnostics.Stopwatch.StartNew();
            while (!speech.Note.Contains(text["speech.voxcpm2.note"], StringComparison.Ordinal) && clock.Elapsed < TimeSpan.FromSeconds(5))
            {
                await Task.Delay(20, TestContext.Current.CancellationToken);
            }

            (speech.NoteIsWarning, speech.NoteIsError, speech.NoteIsOk).ShouldBe((warning, error, ok), report.Adapters[0].Name);
            if (warning)
            {
                speech.Note.ShouldContain(text.Format("speech.memory.cpu", 8));
            }
        }
    }
}
