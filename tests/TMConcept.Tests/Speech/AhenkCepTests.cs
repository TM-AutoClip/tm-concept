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
using TMConcept.Speech.AhenkCep;
using TMConcept.Speech.Speech;
using TMConcept.Tests.Desktop;

namespace TMConcept.Tests.Speech;

/// <summary>
/// Ahenk Cep: its text side (characters, pieces, ids), the frame plan, the noise, the pinned files, the voice in the picker, and one real
/// sentence when the model and the ONNX Runtime library are on this computer (TMCONCEPT_TEST_AHENK_MODEL: the folder that holds onnx/;
/// TMCONCEPT_TEST_ONNXRUNTIME: the folder of onnxruntime; otherwise skipped).
/// </summary>
public sealed class AhenkCepTests
{
    [Fact]
    public void TheTextIsLoweredAndReducedToTheModelsCharacters()
    {
        AhenkCepText.Alphabet("İSTANBUL'da 3 Kişi—geldi…").ShouldBe("istanbul'da kişi-geldi...");
        AhenkCepText.Alphabet("Café ÇAĞ “alıntı”").ShouldBe("cafe çağ \"alıntı\"");
        AhenkCepText.Alphabet("IŞIK  ve\tışık").ShouldBe("ışık ve ışık");
        AhenkCepText.Alphabet("@#$ 🙂").ShouldBe(string.Empty);
    }

    [Fact]
    public void PiecesEndWithAStopAndKnowTheirWords()
    {
        AhenkCepText.Finish("merhaba").ShouldBe("merhaba.");
        AhenkCepText.Finish("merhaba,").ShouldBe("merhaba.");
        AhenkCepText.Finish("ne?").ShouldBe("ne?");
        AhenkCepText.Finish("\"dur!\"").ShouldBe("\"dur!\"");

        var (ids, words, starts) = AhenkCepText.Piece("ab cd");
        ids.ShouldBe([17L, 18, 2, 19, 20]);
        words.ShouldBe([0L, 0, 0, 1, 1]);
        starts.ShouldBe([0L, 0, 0, 3, 3]);
    }

    [Fact]
    public void ALongSceneIsCutAtSentencesWithPauses()
    {
        var sentence = "bu cümle yaklaşık altmış harf uzunluğunda bir deneme cümlesidir. ";
        var pieces = AhenkCepText.Chunk(string.Concat(Enumerable.Repeat(sentence, 8)), 1.0);

        pieces.Count.ShouldBeGreaterThan(1);
        pieces.ShouldAllBe(p => p.Text.Length <= AhenkCepText.MaxLetters && p.Text.EndsWith('.'));
        pieces.SkipLast(1).ShouldAllBe(p => p.Pause == AhenkCepText.SentencePause);
        pieces[^1].Pause.ShouldBe(0);
        AhenkCepText.Chunk("kısa bir cümle", 1.0).ShouldBe([("kısa bir cümle.", 0.0)]);
        AhenkCepText.Pieces("123 !!!", 1.0).ShouldBeEmpty("a piece without a letter is not spoken");
    }

    [Fact]
    public void FramesArePlannedPerWordAndRoundedHalfToEven()
    {
        var (durations, frameWords, positions) = AhenkCepSynthesizer.Plan([2.4f, 2.6f, 1f], [0, 0, 1], 1f);
        durations.ShouldBe([2.4f, 2.6f, 1f]);
        frameWords.ShouldBe([0L, 0, 0, 0, 0, 1]);
        positions.ShouldBe([0f, 0.2f, 0.4f, 0.6f, 0.8f, 0f]);

        var fast = AhenkCepSynthesizer.Plan([2.4f, 2.6f, 1f], [0, 0, 1], 2f);
        fast.FrameWords.ShouldBe([0L, 0, 1], "2.5 rounds to 2, and a word has at least one frame");
    }

    [Fact]
    public void TheNoiseIsSeededAndNormal()
    {
        var a = AhenkCepNoise.Normal(5, 64 * 100);
        a.ShouldBe(AhenkCepNoise.Normal(5, 64 * 100));
        a.ShouldNotBe(AhenkCepNoise.Normal(6, 64 * 100));
        ((double)a.Average()).ShouldBeInRange(-0.1, 0.1);
        Math.Sqrt(a.Select(v => v * (double)v).Average()).ShouldBeInRange(0.9, 1.1);
        AhenkCepNoise.PieceSeed(0, 3).ShouldBe(3UL);
        AhenkCepNoise.PieceSeed(2, 1).ShouldBe(2_000_007UL);
        new AhenkCepNoise(5489).Next().ShouldBe(3499211612u, "the first value of mt19937 with its reference seed");
    }

    [Fact]
    public void Clips48KilohertzAreJoinedWithTheirOwnRate()
    {
        var folder = Path.Combine(TestFolders.Root, "tmc-ahenk-" + Guid.NewGuid().ToString("N"));
        try
        {
            var a = Path.Combine(folder, "a.wav");
            var b = Path.Combine(folder, "b.wav");
            WaveFile.WritePcm16(a, new float[AhenkCepModel.SampleRate / 2], AhenkCepModel.SampleRate);
            WaveFile.WritePcm16(b, new float[AhenkCepModel.SampleRate], AhenkCepModel.SampleRate);

            var length = WaveFile.Join([(a, 250), (b, 0)], Path.Combine(folder, "joined.wav"));

            length.ShouldBe(1750);
            WaveFile.ReadMonoPcm16(Path.Combine(folder, "joined.wav")).SampleRate.ShouldBe(AhenkCepModel.SampleRate);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void EveryFileIsPinnedAtOneCommitWithItsHash()
    {
        AhenkCepModel.Files.Count.ShouldBe(4);
        AhenkCepModel.Files.ShouldAllBe(f => f.Sha256.Length == 64 && f.Url.Host == "huggingface.co"
            && f.Url.AbsolutePath.Contains("/resolve/345c4cbb934815fa96ffd0d5061fd913237544bc/onnx/", StringComparison.Ordinal));
        AhenkCepModel.Files.Select(f => f.Sha256).ShouldBeUnique();
        AhenkCepModel.Files.Sum(f => f.ApproximateBytes).ShouldBe(13_313_110L);
        AhenkCepModel.Vocabulary.Count.ShouldBe(49);
        AhenkCepModel.Styles[0].ShouldBe("neutral");
    }

    [AvaloniaFact]
    public async Task AhenkCepIsOfferedForTurkishOnlyWithSevenStyles()
    {
        using var h = new DesktopHarness(s =>
        {
            s.AddSingleton<IConfirmDialog>(new FakeConfirm());
            s.AddSingleton<IGpuInfo>(new FakeGpu(GpuReport.None));
            s.RemoveAll<IComponentSource>();
            s.AddSingleton<IComponentSource>(new FakeComponents(new FakeComponent(ComponentCodes.SpeechRuntime, ready: true),
                new FakeComponent(ComponentCodes.VoicePrefix + SpeechEngines.AhenkCep, ready: true, ComponentCodes.SpeechRuntime)));
        });
        await FirstRunTests.CreateAsync(h);
        var profile = new Profile { Name = "P", ContentLanguage = ContentLanguages.Turkish, ConceptText = "x" };
        await h.Get<IProfileStore>().SaveAsync(profile, CancellationToken.None);
        var picker = h.Get<StepPickerViewModel>();
        var text = h.Get<Localizer>();

        foreach (var (language, offered) in new[] { (ContentLanguages.Turkish, true), ("de", false), ("en", false) })
        {
            await picker.LoadAsync(profile.Id, language, PipelineSteps.VideoPage);
            picker.Row(PipelineSteps.Speech)!.Options.Any(o => o.Model?.Id == SpeechEngines.AhenkCep).ShouldBe(offered, language);
        }

        await picker.LoadAsync(profile.Id, ContentLanguages.Turkish, PipelineSteps.VideoPage);
        var speech = picker.Row(PipelineSteps.Speech)!;
        speech.Selected = speech.Options.First(o => o.Model?.Id == SpeechEngines.AhenkCep);
        speech.Voices.Select(v => v.Value).ShouldBe(["neutral", "calm", "cheerful", "empathetic", "sad", "angry", "surprised"]);
        speech.Note.ShouldContain(text["speech.ahenk-cep.note"]);
    }

    [AvaloniaFact]
    public async Task TheInstallListShowsOneLineAndTheStyleGoesToTheWorker()
    {
        using var h = new DesktopHarness();
        await FirstRunTests.CreateAsync(h);
        var engine = h.Get<IEnumerable<ISpeechEngine>>().OfType<AhenkCepSpeechEngine>().Single();
        var component = h.Get<ComponentPlanner>().Find(ComponentCodes.VoicePrefix + SpeechEngines.AhenkCep)!;

        component.DependsOn.ShouldBe([ComponentCodes.SpeechRuntime]);
        component.Downloads().Single().ShouldBe(new ComponentDownload("Ahenk Cep " + AhenkCepModel.Version, "huggingface.co", 13_313_110L));
        h.Get<IModelCatalog>().All.ShouldContain(m => m.Provider == ModelProviders.Local && m.Id == SpeechEngines.AhenkCep
            && m.Requires.Contains(ComponentCodes.VoicePrefix + SpeechEngines.AhenkCep));
        engine.Supports(ContentLanguages.Turkish).ShouldBeTrue();
        engine.Supports(ContentLanguages.English).ShouldBeFalse();
        AhenkCepSpeechEngine.StyleOf("sad").ShouldBe(5);
        AhenkCepSpeechEngine.StyleOf("laughter").ShouldBe(0, "the untrained style is never used");
        AhenkCepSpeechEngine.StyleOf("x").ShouldBe(0);

        foreach (var file in AhenkCepModel.Files)
        {
            var path = Path.Combine(engine.ModelFolder, file.ExecutablePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, [1]);
        }

        engine.ModelInstalled.ShouldBeTrue();
        engine.Downloads().ShouldBeEmpty();
    }

    [Fact]
    public void ARealSentenceIsSpokenWhenTheModelIsHere()
    {
        var model = Environment.GetEnvironmentVariable("TMCONCEPT_TEST_AHENK_MODEL");
        var library = Environment.GetEnvironmentVariable("TMCONCEPT_TEST_ONNXRUNTIME");
        if (string.IsNullOrEmpty(model) || string.IsNullOrEmpty(library) || !AhenkCepModel.Files.All(f => File.Exists(Path.Combine(model, f.ExecutablePath))))
        {
            Assert.Skip("The Ahenk Cep model and the ONNX Runtime library are not on this computer.");
        }

        using var synthesizer = new AhenkCepSynthesizer(library, 2, model);
        var samples = synthesizer.Speak("merhaba, bugün hava çok güzel.", 0, 1.0);

        samples.Length.ShouldBeInRange(AhenkCepModel.SampleRate / 2, AhenkCepModel.SampleRate * 6);
        samples.ShouldAllBe(s => float.IsFinite(s));
        samples.Max(Math.Abs).ShouldBeGreaterThan(0.01f, "not silence");
        synthesizer.Last.Pieces.ShouldBe(1);
    }
}
