using Avalonia.Headless.XUnit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Shouldly;
using TMConcept.Core.Components;
using TMConcept.Core.Hardware;
using TMConcept.Core.Models;
using TMConcept.Core.Pipeline;
using TMConcept.Core.Profiles;
using TMConcept.Core.Speech;
using TMConcept.Desktop.Dialogs;
using TMConcept.Desktop.Localization;
using TMConcept.Desktop.ViewModels;
using TMConcept.Speech.Chatterbox;
using TMConcept.Speech.Speech;
using TMConcept.Tests.Desktop;

namespace TMConcept.Tests.Speech;

/// <summary>Chatterbox Multilingual V3: its languages in the picker, the user's voices, the reference file of a scene, the pinned files.</summary>
public sealed class ChatterboxVoiceTests
{
    private static DesktopHarness Harness() => new(s =>
    {
        s.AddSingleton<IConfirmDialog>(new FakeConfirm());
        s.AddSingleton<IGpuInfo>(new FakeGpu(null));
        s.RemoveAll<IVoiceSampleConverter>();
        s.AddSingleton<IVoiceSampleConverter>(new ToneConverter());
        s.RemoveAll<IComponentSource>();
        s.AddSingleton<IComponentSource>(new FakeComponents(new FakeComponent(ComponentCodes.SpeechRuntime, ready: true),
            new FakeComponent(ComponentCodes.VoicePrefix + SpeechEngines.Chatterbox, ready: true, ComponentCodes.SpeechRuntime)));
    });

    /// <summary>"Converts" by writing 6 s of a tone at 24 kHz (no ffmpeg).</summary>
    private sealed class ToneConverter : IVoiceSampleConverter
    {
        public IReadOnlyList<string> RequiredComponents { get; } = [];

        public Task<bool> ConvertAsync(string input, string outputWav, int maxSeconds, CancellationToken cancellationToken)
        {
            var samples = Enumerable.Range(0, 6 * VoiceSampleLimits.SampleRate).Select(i => (float)Math.Sin(i * 0.05) * 0.3f).ToArray();
            WaveFile.WritePcm16(outputWav, samples, VoiceSampleLimits.SampleRate);
            return Task.FromResult(true);
        }
    }

    private static async Task<VoiceSampleInfo> AddSampleAsync(DesktopHarness h)
    {
        var file = Path.Combine(h.Paths.DataDirectory, "me.wav");
        File.WriteAllBytes(file, [1, 2, 3]);
        return (await h.Get<VoiceSampleLibrary>().ImportAsync(file, "Me", CancellationToken.None)).Sample!;
    }

    [AvaloniaFact]
    public async Task ChatterboxIsOfferedForTurkishAndGermanButNotForIndonesianOrRussian()
    {
        using var h = Harness();
        await FirstRunTests.CreateAsync(h);
        var profile = new Profile { Name = "P", ContentLanguage = ContentLanguages.Turkish, ConceptText = "x" };
        await h.Get<IProfileStore>().SaveAsync(profile, CancellationToken.None);
        var picker = h.Get<StepPickerViewModel>();
        var text = h.Get<Localizer>();

        foreach (var (language, offered) in new[] { (ContentLanguages.Turkish, true), ("de", true), ("en", true), ("id", false), ("ru", false) })
        {
            await picker.LoadAsync(profile.Id, language, PipelineSteps.VideoPage);
            picker.Row(PipelineSteps.Speech)!.Options.Any(o => o.Model?.Id == SpeechEngines.Chatterbox).ShouldBe(offered, language);
        }

        await picker.LoadAsync(profile.Id, ContentLanguages.Turkish, PipelineSteps.VideoPage);
        var speech = picker.Row(PipelineSteps.Speech)!;
        speech.Selected = speech.Options.First(o => o.Model?.Id == SpeechEngines.Chatterbox);
        speech.Voices.ShouldBeEmpty("it speaks only with the user's own samples");
        speech.Note.ShouldContain(text["speech.chatterbox.note"]);
        (await picker.PrepareAsync([PipelineSteps.Speech], "install.purpose.video", CancellationToken.None)).ShouldBeFalse();
        picker.Status.ShouldBe(text.Format("picker.not_started.voice", speech.Title));
    }

    [AvaloniaFact]
    public async Task TheSceneGetsTheSampleAndItsLanguageAndTheFileIsRemovedAfterIt()
    {
        using var h = Harness();
        await FirstRunTests.CreateAsync(h);
        var sample = await AddSampleAsync(h);
        var engine = h.Get<IEnumerable<ISpeechEngine>>().OfType<ChatterboxSpeechEngine>().Single();
        engine.Voices("tr").Single().Id.ShouldBe(VoiceSampleLibrary.VoiceId(sample.Id));
        var probe = new EngineProbe(engine);
        var request = new SpeechRequest("Vikingler denize açıldı.", "de", VoiceSampleLibrary.VoiceId(sample.Id), 1.3,
            Path.Combine(h.Paths.WorkDirectory, "a.wav"));

        var worker = await probe.RequestAsync(request);

        worker.Text.ShouldBe(request.Text);
        worker.Language.ShouldBe("de");
        WaveFile.ReadMonoPcm16(worker.ReferenceWav!).SampleRate.ShouldBe(ChatterboxModel.SampleRate);
        probe.Done(worker);
        File.Exists(worker.ReferenceWav).ShouldBeFalse();
        (await Should.ThrowAsync<SpeechException>(() => probe.RequestAsync(request with { Voice = "x" }))).Code.ShouldBe("voice");
    }

    [AvaloniaFact]
    public async Task TheInstallListShowsOneLineAndTheModelIsReadyOnlyWithEveryFile()
    {
        using var h = new DesktopHarness();
        await FirstRunTests.CreateAsync(h);
        var engine = h.Get<IEnumerable<ISpeechEngine>>().OfType<ChatterboxSpeechEngine>().Single();

        var component = h.Get<ComponentPlanner>().Find(ComponentCodes.VoicePrefix + SpeechEngines.Chatterbox)!;

        component.DependsOn.ShouldBe([ComponentCodes.SpeechRuntime]);
        component.Downloads().Single().ShouldBe(new ComponentDownload("Chatterbox Multilingual V3 " + ChatterboxModel.Version, "huggingface.co",
            ChatterboxModel.Files.Sum(f => f.ApproximateBytes)));
        component.Folders().ShouldBe([engine.InstallFolder]);
        h.Get<IModelCatalog>().All.ShouldContain(m => m.Provider == ModelProviders.Local && m.Id == SpeechEngines.Chatterbox
            && m.Requires.Contains(ComponentCodes.VoicePrefix + SpeechEngines.Chatterbox));

        foreach (var file in ChatterboxModel.Files.Skip(1))
        {
            var path = Path.Combine(engine.ModelFolder, file.ExecutablePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, [1]);
        }

        engine.ModelInstalled.ShouldBeFalse("the tokenizer is still missing");
        engine.Downloads().Single().ApproximateBytes.ShouldBe(ChatterboxModel.Files[0].ApproximateBytes);
        File.WriteAllBytes(Path.Combine(engine.ModelFolder, ChatterboxModel.TokenizerFile), [1]);
        engine.ModelInstalled.ShouldBeTrue();
        engine.Downloads().ShouldBeEmpty();
    }

    [Fact]
    public void EveryFileIsPinnedAtOneCommitWithItsHash()
    {
        ChatterboxModel.Files.Count.ShouldBe(10);
        ChatterboxModel.Files.ShouldAllBe(f => f.Sha256.Length == 64 && f.Url.Host == "huggingface.co"
            && f.Url.AbsolutePath.Contains("/resolve/19b94afe41de738624b2f3afc75b4cd56221fbeb/", StringComparison.Ordinal));
        ChatterboxModel.Files.Select(f => f.ExecutablePath).ShouldBeUnique();
        ChatterboxModel.Files.Select(f => f.Sha256).ShouldBeUnique();
        ChatterboxModel.Files.Sum(f => f.ApproximateBytes).ShouldBe(1_555_572_000L);
        ChatterboxModel.Languages.ShouldNotContain("ru");
        ChatterboxModel.Languages.ShouldAllBe(l => ContentLanguages.All.Contains(l));
    }

    /// <summary>Calls the protected request steps of the Chatterbox engine.</summary>
    private sealed class EngineProbe(ChatterboxSpeechEngine engine)
    {
        public Task<SpeechWorkerRequest> RequestAsync(SpeechRequest request) =>
            (Task<SpeechWorkerRequest>)Method("RequestAsync").Invoke(engine, [request, CancellationToken.None])!;

        public void Done(SpeechWorkerRequest request) => Method("Done").Invoke(engine, [request]);

        private static System.Reflection.MethodInfo Method(string name) =>
            typeof(ChatterboxSpeechEngine).GetMethod(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
    }
}
