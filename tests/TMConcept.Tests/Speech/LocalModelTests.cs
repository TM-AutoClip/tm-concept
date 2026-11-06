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
using TMConcept.Core.Text;
using TMConcept.Desktop.Dialogs;
using TMConcept.Desktop.Startup;
using TMConcept.Desktop.ViewModels;
using TMConcept.Speech.Embeddings;
using TMConcept.Speech.Speech;
using TMConcept.Tests.Desktop;
using TMConcept.Tests.Storage;

namespace TMConcept.Tests.Speech;

/// <summary>The second local voice (Kokoro, English) and the local embedding model: offered, installed when used (fake components), and their text handling.</summary>
public sealed class LocalModelTests
{
    [AvaloniaFact]
    public async Task KokoroIsOfferedForEnglishOnlyAndTheLocalMeaningCheckNeedsAnInstall()
    {
        var runtime = new FakeComponent(ComponentCodes.SpeechRuntime, ready: false);
        var kokoro = new FakeComponent(ComponentCodes.VoicePrefix + SpeechEngines.Kokoro, ready: false, ComponentCodes.SpeechRuntime);
        var embedding = new FakeComponent(ComponentCodes.EmbeddingPrefix + LocalEmbeddingModels.Id, ready: false, ComponentCodes.SpeechRuntime);
        var confirm = new FakeConfirm();
        using var h = new DesktopHarness(s =>
        {
            s.AddSingleton<IConfirmDialog>(confirm);
            s.AddSingleton<IGpuInfo>(new FakeGpu(null));
            s.RemoveAll<IComponentSource>();
            s.AddSingleton<IComponentSource>(new FakeComponents(runtime, kokoro, embedding));
        });
        await FirstRunTests.CreateAsync(h);
        var english = new Profile { Name = "En", ContentLanguage = ContentLanguages.English, ConceptText = "x" };
        var turkish = new Profile { Name = "Tr", ContentLanguage = ContentLanguages.Turkish, ConceptText = "x" };
        await h.Get<IProfileStore>().SaveAsync(english, CancellationToken.None);
        await h.Get<IProfileStore>().SaveAsync(turkish, CancellationToken.None);

        var picker = h.Get<StepPickerViewModel>();
        await picker.LoadAsync(turkish.Id, ContentLanguages.Turkish, PipelineSteps.VideoPage);
        picker.Row(PipelineSteps.Speech)!.Options.ShouldNotContain(o => o.Model != null && o.Model.Id == SpeechEngines.Kokoro, "Kokoro reads English only");
        picker.Row(PipelineSteps.Speech)!.Options.ShouldContain(o => o.Model != null && o.Model.Id == SpeechEngines.Supertonic);

        await picker.LoadAsync(english.Id, ContentLanguages.English, PipelineSteps.VideoPage);
        var speech = picker.Row(PipelineSteps.Speech)!;
        speech.Selected = speech.Options.First(o => o.Model?.Id == SpeechEngines.Kokoro);
        speech.Voices.First().Label.ShouldBe("Heart (American, female)");
        speech.Note.ShouldContain(h.Get<TMConcept.Desktop.Localization.Localizer>()["models.installs_on_start"]);
        (await picker.PrepareAsync([PipelineSteps.Speech], "install.purpose.video", CancellationToken.None)).ShouldBeTrue();
        confirm.Asked.Count.ShouldBe(1, "Kokoro (Apache-2.0) asks for no license, only the download list");
        (runtime.Installs, kokoro.Installs).ShouldBe((1, 1));

        // The concept page offers the local meaning check first; choosing it installs it at the start.
        var concepts = h.Get<StepPickerViewModel>();
        await concepts.LoadAsync(turkish.Id, ContentLanguages.Turkish, PipelineSteps.ConceptPage);
        var semantic = concepts.Row(PipelineSteps.Embedding)!;
        semantic.Options[1].Model!.Id.ShouldBe(LocalEmbeddingModels.Id);
        semantic.Selected = semantic.Options[1];
        (await concepts.PrepareAsync([PipelineSteps.Embedding], "install.purpose.concepts", CancellationToken.None)).ShouldBeTrue();
        embedding.Installs.ShouldBe(1);
        (await h.Get<IStepModelStore>().GetAsync(PipelineSteps.Embedding, turkish.Id, CancellationToken.None))!.Model.ShouldBe(LocalEmbeddingModels.Id);
    }

    [Fact]
    public void NumbersAreSpelledOutForAnEnglishVoice()
    {
        EnglishNumbers.Spell("In 672 about 1,500 ships, 3.5 % of them.").ShouldBe(
            "In six hundred seventy-two about one thousand five hundred ships, three point five percent of them.");
        EnglishNumbers.Words(2_000_017).ShouldBe("two million seventeen");
        EnglishNumbers.Read("1234567890123").ShouldBe("one two three four five six seven eight nine zero one two three");
    }

    [Fact]
    public void TheKokoroRequestSpellsNumbersAndKeepsAnEnglishVoice()
    {
        using var paths = new TempPaths();
        var request = new KokoroRequestProbe().Make(new TMConcept.Core.Speech.SpeechRequest("Year 1453.", "en", "999", 1.2, Path.Combine(paths.WorkDirectory, "a.wav"), null));
        request.Text.ShouldBe("Year one thousand four hundred fifty-three.");
        request.Speaker.ShouldBe(KokoroSpeechEngine.EnglishVoices[0].Id, "an unknown voice falls back to the first English one");
    }

    [Fact]
    public void TheUnigramTokenizerFindsTheMostLikelyPieces()
    {
        using var paths = new TempPaths();
        var file = Path.Combine(paths.DataDirectory, "tokenizer.json");
        File.WriteAllText(file, """
            {"model":{"type":"Unigram","unk_id":3,"vocab":[["<s>",0],["<pad>",0],["</s>",0],["<unk>",0],["▁wall",-2],["▁wa",-5],["ll",-5],["s",-1],["▁",-3]]}}
            """);
        var tokenizer = UnigramTokenizer.Load(file);

        tokenizer.Encode("walls").ShouldBe([0, 4, 7, 2]);
        tokenizer.Encode("walls  ✓").ShouldBe([0, 4, 7, 8, 3, 2], "an unknown character becomes one unknown piece");
        tokenizer.Encode(string.Join(' ', Enumerable.Repeat("walls", 200))).Length.ShouldBe(UnigramTokenizer.MaxTokens);
    }

    /// <summary>Calls the protected request builder of the Kokoro engine.</summary>
    private sealed class KokoroRequestProbe
    {
        public SpeechWorkerRequest Make(TMConcept.Core.Speech.SpeechRequest request)
        {
            var method = typeof(KokoroSpeechEngine).GetMethod("Request", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            var engine = (KokoroSpeechEngine)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(KokoroSpeechEngine));
            return (SpeechWorkerRequest)method.Invoke(engine, [request])!;
        }
    }
}
