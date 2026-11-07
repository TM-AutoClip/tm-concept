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
using TMConcept.Speech.Speech;
using TMConcept.Tests.Desktop;
using TMConcept.Tests.Storage;

namespace TMConcept.Tests.Speech;

/// <summary>Pocket TTS with the user's own voice: samples added, listed as voices, written for a scene and removed after it; its install.</summary>
public sealed class VoiceCloneTests
{
    /// <summary>"Converts" by writing <see cref="Seconds"/> of a tone (no ffmpeg).</summary>
    private sealed class FakeConverter : IVoiceSampleConverter
    {
        public double Seconds { get; set; } = 6;

        public bool Fail { get; set; }

        public int Calls { get; private set; }

        public IReadOnlyList<string> RequiredComponents { get; } = [ComponentCodes.Ffmpeg];

        public Task<bool> ConvertAsync(string input, string outputWav, int maxSeconds, CancellationToken cancellationToken)
        {
            Calls++;
            if (Fail)
            {
                return Task.FromResult(false);
            }

            var count = (int)(Math.Min(Seconds, maxSeconds) * VoiceSampleLimits.SampleRate);
            var samples = Enumerable.Range(0, count).Select(i => (float)Math.Sin(i * (Calls + 1) * 0.05) * 0.3f).ToArray();
            WaveFile.WritePcm16(outputWav, samples, VoiceSampleLimits.SampleRate);
            return Task.FromResult(true);
        }
    }

    private static DesktopHarness Harness(FakeConverter converter) => new(s =>
    {
        s.AddSingleton<IConfirmDialog>(new FakeConfirm());
        s.AddSingleton<IGpuInfo>(new FakeGpu(null));
        s.RemoveAll<IVoiceSampleConverter>();
        s.AddSingleton<IVoiceSampleConverter>(converter);
        s.RemoveAll<IComponentSource>();
        s.AddSingleton<IComponentSource>(new FakeComponents(new FakeComponent(ComponentCodes.Ffmpeg, ready: true),
            new FakeComponent(ComponentCodes.SpeechRuntime, ready: true),
            new FakeComponent(ComponentCodes.VoicePrefix + SpeechEngines.Pocket, ready: true, ComponentCodes.SpeechRuntime)));
    });

    private static string Recording(DesktopHarness h)
    {
        var file = Path.Combine(h.Paths.DataDirectory, "my voice.mp3");
        File.WriteAllBytes(file, [1, 2, 3]);
        return file;
    }

    [AvaloniaFact]
    public async Task ASampleIsAddedOnceAndShortOrUnreadableRecordingsAreRefused()
    {
        var converter = new FakeConverter();
        using var h = Harness(converter);
        await FirstRunTests.CreateAsync(h);
        var library = h.Get<VoiceSampleLibrary>();
        var file = Recording(h);

        (await library.ImportAsync(file, " ", CancellationToken.None)).Problem.ShouldBe("name");
        (await library.ImportAsync(Path.Combine(h.Paths.DataDirectory, "none.wav"), "x", CancellationToken.None)).Problem.ShouldBe("file");
        var added = await library.ImportAsync(file, "My voice", CancellationToken.None);
        added.Sample!.DurationMs.ShouldBe(6000);
        library.Samples.Single().Name.ShouldBe("My voice");

        converter.Seconds = 2;
        (await library.ImportAsync(file, "Short", CancellationToken.None)).Problem.ShouldBe("short");
        converter.Seconds = 60;
        (await library.ImportAsync(file, "Long", CancellationToken.None)).Sample!.DurationMs.ShouldBe(VoiceSampleLimits.MaxSeconds * 1000L, "cut to 15 s");
        converter.Fail = true;
        (await library.ImportAsync(file, "Broken", CancellationToken.None)).Problem.ShouldBe("read");
        Directory.GetFiles(Path.Combine(h.Paths.WorkDirectory, "voices")).ShouldBeEmpty("no work file of the user's voice is left");

        await library.DeleteAsync(added.Sample.Id, CancellationToken.None);
        library.Samples.Select(s => s.Name).ShouldBe(["Long"]);
    }

    [AvaloniaFact]
    public async Task PocketIsEnglishOnlyAndListsTheUsersVoicesAndWaitsForOne()
    {
        var converter = new FakeConverter();
        using var h = Harness(converter);
        await FirstRunTests.CreateAsync(h);
        var profile = new Profile { Name = "P", ContentLanguage = ContentLanguages.English, ConceptText = "x" };
        await h.Get<IProfileStore>().SaveAsync(profile, CancellationToken.None);
        var picker = h.Get<StepPickerViewModel>();
        var text = h.Get<Localizer>();

        await picker.LoadAsync(profile.Id, ContentLanguages.German, PipelineSteps.VideoPage);
        picker.Row(PipelineSteps.Speech)!.Options.ShouldNotContain(o => o.Model != null && o.Model.Id == SpeechEngines.Pocket);

        await picker.LoadAsync(profile.Id, ContentLanguages.English, PipelineSteps.VideoPage);
        var speech = picker.Row(PipelineSteps.Speech)!;
        speech.Selected = speech.Options.First(o => o.Model?.Id == SpeechEngines.Pocket);
        speech.Voices.ShouldBeEmpty();
        speech.Note.ShouldContain(text["speech.voices.none"]);
        (await picker.PrepareAsync([PipelineSteps.Speech], "install.purpose.video", CancellationToken.None)).ShouldBeFalse();
        picker.Status.ShouldBe(text.Format("picker.not_started.voice", speech.Title));

        // An own voice added on the settings page is a voice of the step the next time the page is shown.
        var settings = h.Get<VoiceSettingsViewModel>();
        await settings.ActivateAsync();
        settings.SampleName = "My voice";
        await settings.ImportAsync(Recording(h));
        settings.Samples.Single().Name.ShouldBe("My voice");
        settings.SampleStatus.ShouldBe(text.Format("voices.samples.added", "My voice", "6"));

        await picker.LoadAsync(profile.Id, ContentLanguages.English, PipelineSteps.VideoPage);
        speech = picker.Row(PipelineSteps.Speech)!;
        speech.Selected = speech.Options.First(o => o.Model?.Id == SpeechEngines.Pocket);
        speech.Voice!.Label.ShouldBe("My voice (6 s)");
        speech.Voice.Value.ShouldBe(VoiceSampleLibrary.VoiceId(settings.Samples[0].Id));
        (await picker.PrepareAsync([PipelineSteps.Speech], "install.purpose.video", CancellationToken.None)).ShouldBeTrue();
        StepModelResolver.Options(await h.Get<IStepModelStore>().GetAsync(PipelineSteps.Speech, profile.Id, CancellationToken.None)).Voice
            .ShouldBe(speech.Voice.Value);

        await settings.DeleteSampleAsync(settings.Samples[0]);
        settings.Samples.ShouldBeEmpty();
    }

    [AvaloniaFact]
    public async Task TheSampleIsWrittenForTheSceneAndRemovedAfterIt()
    {
        var converter = new FakeConverter();
        using var h = Harness(converter);
        await FirstRunTests.CreateAsync(h);
        var library = h.Get<VoiceSampleLibrary>();
        var sample = (await library.ImportAsync(Recording(h), "Me", CancellationToken.None)).Sample!;
        var engine = h.Get<IEnumerable<ISpeechEngine>>().OfType<PocketSpeechEngine>().Single();
        var probe = new PocketProbe(engine);
        var request = new SpeechRequest("Year 1453.", "en", VoiceSampleLibrary.VoiceId(sample.Id), 1.4, Path.Combine(h.Paths.WorkDirectory, "a.wav"));

        var worker = await probe.RequestAsync(request);

        worker.Text.ShouldBe("Year 1453.", "Pocket reads digits itself");
        worker.Steps.ShouldBe(PocketSpeechEngine.Steps);
        var (samples, rate) = WaveFile.ReadMonoPcm16(worker.ReferenceWav!);
        (samples.Length, rate).ShouldBe((6 * VoiceSampleLimits.SampleRate, VoiceSampleLimits.SampleRate));
        probe.Done(worker);
        File.Exists(worker.ReferenceWav).ShouldBeFalse();

        (await Should.ThrowAsync<SpeechException>(() => probe.RequestAsync(request with { Voice = "3" }))).Code.ShouldBe("voice");
        await library.DeleteAsync(sample.Id, CancellationToken.None);
        (await Should.ThrowAsync<SpeechException>(() => probe.RequestAsync(request))).Code.ShouldBe("voice");
    }

    [Fact]
    public void ThePocketModelIsPinnedAndItsTestVoicesAreNotKept()
    {
        SpeechModels.Pocket.Sha256.ShouldBe("2f3b88823cbbb9bf0b2477ec8ae7b3fec417b3a87b6bb5f256dba66f2ad967cb");
        SpeechModels.Pocket.Url.Host.ShouldBe("github.com");
        SpeechModels.Unused.ShouldContain("test_wavs");
        VoiceSampleLibrary.SampleOf(VoiceSampleLibrary.VoiceId(Guid.Empty)).ShouldBe(Guid.Empty);
        VoiceSampleLibrary.SampleOf("3").ShouldBeNull();
    }

    [AvaloniaFact]
    public async Task TheInstallListShowsThePocketDownload()
    {
        using var h = new DesktopHarness();
        await FirstRunTests.CreateAsync(h);

        var pocket = h.Get<ComponentPlanner>().Find(ComponentCodes.VoicePrefix + SpeechEngines.Pocket)!;

        pocket.DependsOn.ShouldBe([ComponentCodes.SpeechRuntime]);
        pocket.Downloads().Single().ShouldBe(new TMConcept.Core.Components.ComponentDownload("Pocket TTS int8-2026-01-26", "github.com", 98_336_520));
        h.Get<IModelCatalog>().All.ShouldContain(m => m.Provider == ModelProviders.Local && m.Id == SpeechEngines.Pocket
            && m.Requires.Contains(ComponentCodes.VoicePrefix + SpeechEngines.Pocket));
    }

    [Fact]
    public void AMonoSixteenBitFileIsReadBackAndOtherFormatsAreRefused()
    {
        using var paths = new TempPaths();
        var file = Path.Combine(paths.WorkDirectory, "t.wav");
        WaveFile.WritePcm16(file, [0.5f, -0.25f, 0f], 22_050);

        var (samples, rate) = WaveFile.ReadMonoPcm16(file);

        rate.ShouldBe(22_050);
        samples.Length.ShouldBe(3);
        samples[0].ShouldBe(0.5f, 0.001f);
        samples[1].ShouldBe(-0.25f, 0.001f);
        File.WriteAllBytes(file, [.. "RIFF"u8, 0, 0, 0, 0, .. "WAVEfmt "u8, 16, 0, 0, 0, 3, 0, 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 32, 0, .. "data"u8, 0, 0, 0, 0]);
        Should.Throw<InvalidDataException>(() => WaveFile.ReadMonoPcm16(file));
    }

    /// <summary>Calls the protected request steps of the Pocket engine.</summary>
    private sealed class PocketProbe(PocketSpeechEngine engine)
    {
        public Task<SpeechWorkerRequest> RequestAsync(SpeechRequest request) =>
            (Task<SpeechWorkerRequest>)Method("RequestAsync").Invoke(engine, [request, CancellationToken.None])!;

        public void Done(SpeechWorkerRequest request) => Method("Done").Invoke(engine, [request]);

        private static System.Reflection.MethodInfo Method(string name) =>
            typeof(PocketSpeechEngine).GetMethod(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
    }
}
