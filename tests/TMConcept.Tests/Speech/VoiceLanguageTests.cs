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
using TMConcept.Tests.Desktop;

namespace TMConcept.Tests.Speech;

/// <summary>Voices by the profile's video language: only voices that speak it, a default per language, a warning when a saved one falls away.</summary>
public sealed class VoiceLanguageTests
{
    private static DesktopHarness Harness() => new(s =>
    {
        s.AddSingleton<IConfirmDialog>(new FakeConfirm());
        s.AddSingleton<IGpuInfo>(new FakeGpu(null));
        s.RemoveAll<IComponentSource>();
        s.AddSingleton<IComponentSource>(new FakeComponents(new FakeComponent(ComponentCodes.SpeechRuntime, ready: true),
            new FakeComponent(ComponentCodes.VoicePrefix + SpeechEngines.Supertonic, ready: true, ComponentCodes.SpeechRuntime),
            new FakeComponent(ComponentCodes.VoicePrefix + SpeechEngines.Kokoro, ready: true, ComponentCodes.SpeechRuntime)));
    });

    private static async Task<Profile> ProfileAsync(DesktopHarness h, string language, string model, string voice)
    {
        await FirstRunTests.CreateAsync(h);
        var profile = new Profile { Name = "P", ContentLanguage = language, ConceptText = "x" };
        await h.Get<IProfileStore>().SaveAsync(profile, CancellationToken.None);
        await h.Get<IStepModelStore>().SaveAsync(new StepModelChoice
        {
            Step = PipelineSteps.Speech,
            ProfileId = profile.Id,
            Provider = ModelProviders.Local,
            Model = model,
            OptionsJson = StepModelResolver.OptionsJson(new StepOptions { Voice = voice }),
        }, CancellationToken.None);
        return profile;
    }

    [AvaloniaFact]
    public async Task AVoiceThatDoesNotSpeakTheNewLanguageFallsBackToItsDefaultWithAWarning()
    {
        using var h = Harness();
        var profile = await ProfileAsync(h, ContentLanguages.German, SpeechEngines.Kokoro, "3");
        var picker = h.Get<StepPickerViewModel>();

        await picker.LoadAsync(profile.Id, ContentLanguages.German, PipelineSteps.VideoPage);

        var speech = picker.Row(PipelineSteps.Speech)!;
        speech.Options.ShouldNotContain(o => o.Model != null && o.Model.Id == SpeechEngines.Kokoro, "Kokoro speaks English only");
        speech.Selected!.Model!.Id.ShouldBe(SpeechEngines.Supertonic, "Supertonic is the default voice of German");
        speech.NoteIsWarning.ShouldBeTrue();
        speech.Note.ShouldStartWith(h.Get<Localizer>().Format("speech.fallback.language", "Kokoro 82M (English)",
            OutputLanguages.NameIn(ContentLanguages.German, h.Get<Localizer>().Language), "Supertonic 3"));
        speech.Recommended.ShouldNotContain("Kokoro");

        // The replacement is saved at the next prepare, so a video never uses the English-only voice.
        (await picker.PrepareAsync([PipelineSteps.Speech], "install.purpose.video", CancellationToken.None)).ShouldBeTrue();
        (await h.Get<IStepModelStore>().GetAsync(PipelineSteps.Speech, profile.Id, CancellationToken.None))!.Model.ShouldBe(SpeechEngines.Supertonic);

        // Choosing something clears the note about the replaced voice.
        speech.Voice = speech.Voices[2];
        speech.Note.ShouldNotContain("Kokoro 82M (English)");
    }

    [AvaloniaFact]
    public async Task EnglishSuggestsKokoroFirstAndAGoneVoiceIsReplacedBySaying()
    {
        using var h = Harness();
        var profile = await ProfileAsync(h, ContentLanguages.English, SpeechEngines.Supertonic, "42");
        var picker = h.Get<StepPickerViewModel>();

        await picker.LoadAsync(profile.Id, ContentLanguages.English, PipelineSteps.VideoPage);

        var speech = picker.Row(PipelineSteps.Speech)!;
        speech.Recommended.IndexOf("Kokoro", StringComparison.Ordinal).ShouldBeLessThan(speech.Recommended.IndexOf("Supertonic", StringComparison.Ordinal));
        speech.Selected!.Model!.Id.ShouldBe(SpeechEngines.Supertonic, "the model speaks English; only its voice is gone");
        speech.Voice!.Value.ShouldBe("0");
        speech.Note.ShouldStartWith(h.Get<Localizer>().Format("speech.fallback.voice", speech.Voice.Label));
        speech.NoteIsWarning.ShouldBeTrue();
    }

    [Fact]
    public void TheDefaultVoiceIsKokoroForEnglishAndSupertonicForTheOthers()
    {
        SpeechDefaults.EngineFor(ContentLanguages.English).ShouldBe(SpeechEngines.Kokoro);
        foreach (var language in ContentLanguages.All.Where(l => l != ContentLanguages.English))
        {
            SpeechDefaults.EngineFor(language).ShouldBe(SpeechEngines.Supertonic, language);
        }

        SpeechDefaults.ModelFor(ContentLanguages.Turkish).ShouldBe("local/supertonic");
    }
}
