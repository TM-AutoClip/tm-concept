using Avalonia.Headless.XUnit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Shouldly;
using TMConcept.Core.Profiles;
using TMConcept.Core.Pronunciation;
using TMConcept.Core.Speech;
using TMConcept.Core.Tools;
using TMConcept.Desktop.Localization;
using TMConcept.Desktop.Os;
using TMConcept.Desktop.ViewModels;

namespace TMConcept.Tests.Desktop;

/// <summary>
/// Settings → Voice and pronunciation: the categorised built-in list, search, editing a built-in word into the user's own, exact-case
/// words, and listening with an installed local voice (the word in the boxes applies before it is saved; nothing is downloaded).
/// </summary>
public sealed class PronunciationSettingsTests
{
    private static DesktopHarness Harness(FakeVoice voice, FakeLauncher launcher) => new(s =>
    {
        s.RemoveAll<ISpeechEngine>();
        s.AddSingleton<ISpeechEngine>(voice);
        s.RemoveAll<IShellLauncher>();
        s.AddSingleton<IShellLauncher>(launcher);
    });

    private static async Task<VoiceSettingsViewModel> TurkishAsync(DesktopHarness h)
    {
        await FirstRunTests.CreateAsync(h);
        var section = h.Get<VoiceSettingsViewModel>();
        await section.ActivateAsync();
        section.Language = section.Languages.First(l => l.Value == ContentLanguages.Turkish);
        await section.ActivateAsync();
        return section;
    }

    [AvaloniaFact]
    public async Task TheListIsSearchedAndABuiltInWordIsEditedIntoTheUsersOwn()
    {
        using var h = Harness(new FakeVoice(ready: true), new FakeLauncher());
        var section = await TurkishAsync(h);
        var text = h.Get<Localizer>();

        section.Words.Count.ShouldBeGreaterThan(400);
        section.Counts.ShouldBe(text.Format("voices.words.counts", section.Words.Count, 0));
        section.Words.ShouldContain(w => w.Shown == "!IT" && w.ExactCase);
        section.Words.First(w => w.Word == "Google").Tag.ShouldContain(text["voices.words.builtin"]);

        section.Search = "çörç";
        section.Words.Select(w => w.Word).ShouldBe(["Churchill", "Winston Churchill"], ignoreOrder: true);
        section.Search = "Yerler";
        section.Words.ShouldContain(w => w.Word == "Machu Picchu", "a category matches too");
        section.Search = string.Empty;

        section.EditWord(section.Words.First(w => w.Word == "Google"));
        section.NewWord.ShouldBe("Google");
        section.AddLabel.ShouldBe(text["voices.words.save"]);
        section.NewSpoken = "Guugıl";
        await section.AddWordAsync();

        section.Words[0].ShouldSatisfyAllConditions(w => w.Word.ShouldBe("Google"), w => w.Spoken.ShouldBe("Guugıl"), w => w.Own.ShouldBeTrue());
        section.Words.Count(w => w.Word == "Google").ShouldBe(1, "the user's word replaces the built-in one");
        section.AddLabel.ShouldBe(text["voices.words.add"]);

        section.EditWord(section.Words[0]);
        section.NewWord = "!GOOGLE";
        await section.AddWordAsync();
        section.Words.Where(w => w.Own).Select(w => w.Shown).ShouldBe(["!GOOGLE"], "an own word saved under another spelling replaces the old one");
        (await h.Get<PronunciationService>().PrepareAsync("GOOGLE ve Google", ContentLanguages.Turkish, CancellationToken.None))
            .ShouldBe("Guugıl ve Gugıl");
    }

    [AvaloniaFact]
    public async Task ListeningUsesTheInstalledVoiceAndTheWordInTheBoxes()
    {
        var voice = new FakeVoice(ready: true);
        var launcher = new FakeLauncher();
        using var h = Harness(voice, launcher);
        var section = await TurkishAsync(h);

        section.ListenVoices.Select(v => v.Value).ShouldBe([SpeechEngines.AhenkCep]);
        section.NewWord = "Harald";
        section.NewSpoken = "Haralt";
        section.ListenText = "Harald 1066'da öldü.";
        await section.ListenAsync();

        voice.Spoken.ShouldBe(["Haralt bin altmış altıda öldü."], "the unsaved word and the Turkish rules apply");
        launcher.Paths.Single().ShouldEndWith("pronunciation.wav");
        (await h.Get<PronunciationService>().RulesAsync(ContentLanguages.Turkish, CancellationToken.None)).ShouldNotContain(r => r.Word == "Harald");
    }

    [AvaloniaFact]
    public async Task WithoutAnInstalledVoiceListeningSaysWhy()
    {
        var voice = new FakeVoice(ready: false);
        using var h = Harness(voice, new FakeLauncher());
        var section = await TurkishAsync(h);

        section.ListenVoices.ShouldBeEmpty("a voice that is not installed is never downloaded from here");
        section.ListenText = "Merhaba";
        await section.ListenAsync();

        section.Status.ShouldBe(h.Get<Localizer>()["voices.listen.no_voice"]);
        voice.Spoken.ShouldBeEmpty();
    }

    /// <summary>A local Turkish voice that writes a short silent file and records what it was given.</summary>
    private sealed class FakeVoice(bool ready) : ISpeechEngine
    {
        public List<string> Spoken { get; } = [];

        public string Code => SpeechEngines.AhenkCep;

        public string Name => "Ahenk Cep";

        public bool IsLocal => true;

        public bool Ready => ready;

        public bool Supports(string language) => language == ContentLanguages.Turkish;

        public IReadOnlyList<SpeechVoice> Voices(string language) => [new("neutral", "Düz")];

        public Task InstallAsync(bool agreed, IProgress<ToolProgress>? progress, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<SpeechClip> SynthesizeAsync(SpeechRequest request, CancellationToken cancellationToken)
        {
            Spoken.Add(request.Text);
            WaveFile.WritePcm16(request.OutputWav, new float[480], 48_000);
            return Task.FromResult(new SpeechClip(request.OutputWav, 10, request.Text.Length, 0, Code));
        }
    }

    /// <summary>Records what would have been opened; opens nothing.</summary>
    private sealed class FakeLauncher : IShellLauncher
    {
        public List<string> Paths { get; } = [];

        public void Open(Uri address)
        {
        }

        public void OpenPath(string path) => Paths.Add(path);

        public void Reveal(string file)
        {
        }
    }
}
