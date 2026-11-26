using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using TMConcept.Core.Keys;
using TMConcept.Core.Models;
using TMConcept.Core.Music;
using TMConcept.Providers.Catalog;
using TMConcept.Tests.Storage;

namespace TMConcept.Tests.Core;

/// <summary>
/// 0.1.55, "Program composes" (music from code): the prompt's words choose the mood, style and tempo (Turkish and English), the composer gives
/// the same score for the same brief and seed and a valid one for every mood and style, the MIDI file follows the Standard MIDI File layout,
/// and the generator writes the MIDI file, has it played and never takes the graphics card. No synthesizer runs here
/// (<see cref="Engines.MusicSynthSmokeTests"/> plays a real one when a SoundFont is given).
/// </summary>
public sealed class ProgramScoreTests
{
    private static readonly CatalogModel Model = new()
    {
        Provider = ModelProviders.Local, Id = ScoreRules.ModelId, Name = "Program composes", Capabilities = ModelCapability.Music,
        Requires = ["node", "music-synth", "soundfont.musescore-general"],
    };

    [Theory]
    [InlineData("antik Mısır'da disko, gizemli ama dans edilebilir", ScoreMood.Mysterious, ScoreStyle.Electronic, ScoreStyle.World)]
    [InlineData("hüzünlü piyano, yavaş", ScoreMood.Sad, ScoreStyle.Acoustic, null)]
    [InlineData("epic orchestral battle music", ScoreMood.Epic, ScoreStyle.Orchestral, null)]
    [InlineData("Bizans sarayında gizemli bir gece", ScoreMood.Mysterious, ScoreStyle.Medieval, ScoreStyle.Medieval)]
    [InlineData("sadece sakin bir şey", ScoreMood.Calm, ScoreStyle.Cinematic, null)]
    [InlineData("", ScoreMood.Calm, ScoreStyle.Cinematic, null)]
    public void ThePromptsWordsChooseTheMoodTheStyleAndTheScale(string prompt, ScoreMood mood, ScoreStyle style, ScoreStyle? flavor)
    {
        var traits = ScorePromptReader.Read(prompt, null);

        traits.Mood.ShouldBe(mood);
        traits.Style.ShouldBe(style);
        traits.Flavor.ShouldBe(flavor);
    }

    [Fact]
    public void TheTempoIsTheOneAskedForThenATempoWordThenTheMoods()
    {
        ScorePromptReader.Read("calm piano", 150).Bpm.ShouldBe(150, "a reference sound's or the video's tempo wins");
        ScorePromptReader.Read("calm piano", 500).Bpm.ShouldBe(ScoreRules.MaxBpm);
        ScorePromptReader.Read("yavaş bir ezgi", null).Bpm.ShouldBe(ScoreRules.SlowBpm);
        ScorePromptReader.Read("fast synth", null).Bpm.ShouldBe(ScoreRules.FastBpm);
        ScorePromptReader.Read("aksiyon", null).Bpm.ShouldBe(ScoreMoods.All[ScoreMood.Action].Bpm);
        ScorePromptReader.Matches("sadece", "sad").ShouldBeFalse("a short word counts only as a whole word");
        ScorePromptReader.Matches("huzunlu", "huzun").ShouldBeTrue("a long word is a word start");
    }

    [Fact]
    public void TheMoodsAndStylesAreTheMusicTagsWordsAndEveryOneHasItsTables()
    {
        Enum.GetNames<ScoreMood>().Select(n => n.ToLowerInvariant()).ShouldBe(MusicTags.Moods);
        Enum.GetNames<ScoreStyle>().Select(n => n.ToLowerInvariant()).ShouldBe(MusicTags.Styles);
        ScoreMoods.All.Keys.ShouldBe(Enum.GetValues<ScoreMood>(), ignoreOrder: true);
        ScoreWords.Moods.Keys.ShouldBe(Enum.GetValues<ScoreMood>(), ignoreOrder: true);
        ScoreStyles.All.Keys.ShouldBe(Enum.GetValues<ScoreStyle>(), ignoreOrder: true);
        ScoreWords.Styles.Keys.ShouldBe(Enum.GetValues<ScoreStyle>(), ignoreOrder: true);
        ScoreScales.Flavors.Keys.ShouldBe(ScoreStyles.Flavors, ignoreOrder: true);
    }

    [Fact]
    public void TheSameBriefAndSeedGiveTheSameScoreAnotherSeedAnotherOne()
    {
        var writer = new ProgramScoreWriter();
        var brief = new ScoreBrief("uplifting acoustic", 45, null, 7);

        var first = MidiWriter.Write(writer.Write(brief));
        var again = MidiWriter.Write(writer.Write(brief));
        var other = MidiWriter.Write(writer.Write(brief with { Seed = 8 }));

        again.ShouldBe(first);
        other.ShouldNotBe(first);
    }

    [Fact]
    public void EveryMoodAndStyleGivesAValidScoreAtLeastAsLongAsAsked()
    {
        var writer = new ProgramScoreWriter();
        foreach (var mood in MusicTags.Moods)
        {
            foreach (var style in MusicTags.Styles)
            {
                foreach (var seconds in new[] { 10, 60, 120 })
                {
                    var score = writer.Write(new ScoreBrief(mood + " " + style, seconds, null, seconds));

                    ScoreValidator.Problems(score).ShouldBeEmpty($"{mood} {style} {seconds} s");
                    score.Seconds.ShouldBeGreaterThanOrEqualTo(seconds);
                    score.Seconds.ShouldBeLessThan(Math.Max(seconds + (ScoreRules.BeatsPerBar * 60.0 / score.Bpm), (ScoreRules.IntroBars + ScoreRules.MotifBars)
                        * ScoreRules.BeatsPerBar * 60.0 / score.Bpm) + 0.001, "whole bars, not more");
                    score.Tracks.Select(t => t.Channel).ShouldBeUnique();
                }
            }
        }
    }

    [Fact]
    public void ThePieceEndsOnTheKeysOwnChordAndTheStylePicksTheParts()
    {
        var writer = new ProgramScoreWriter();
        var calm = writer.Write(new ScoreBrief("calm ambient", 60, null, 3));
        var dance = writer.Write(new ScoreBrief("electronic", 60, null, 3));
        var pad = calm.Tracks.Single(t => t.Name == "Pad").Notes;
        var bar = ScoreRules.TicksPerQuarter * ScoreRules.BeatsPerBar;

        // Every progression starts on the key's own chord, and the last bar goes back to it.
        pad.Where(n => n.Start == (calm.Bars - 1) * bar).Select(n => n.Pitch).ShouldBe(pad.Where(n => n.Start == 0).Select(n => n.Pitch));
        calm.Tracks.ShouldNotContain(t => t.Name == "Lead", "an ambient bed has no tune");
        calm.Tracks.ShouldNotContain(t => t.Channel == ScoreRules.DrumChannel, "a calm ambient bed has no drums");
        dance.Tracks.ShouldContain(t => t.Channel == ScoreRules.DrumChannel && t.Notes.Count > 0);
        dance.Tracks.Single(t => t.Name == "Lead").Notes.ShouldAllBe(n => n.Pitch >= ScoreRules.LeadLow && n.Pitch <= ScoreRules.LeadHigh);
        dance.Tracks.Single(t => t.Name == "Lead").Notes.ShouldAllBe(n => n.Start >= ScoreRules.IntroBars * bar, "the tune waits for the intro");
    }

    [Fact]
    public void TheValidatorNamesWhatIsWrong()
    {
        var bad = new MusicScore(10, ScoreRules.TicksPerQuarter, ScoreRules.BeatsPerBar, 2,
        [
            new ScoreTrack("A", 0, 200, 100, 64, [new ScoreNote(0, 480, 60, 0)]),
            new ScoreTrack("B", 0, 1, 100, 64, [new ScoreNote(3000, 9000, 60, 90)]),
        ]);

        var problems = ScoreValidator.Problems(bad);

        problems.ShouldContain(p => p.Contains("tempo 10"));
        problems.ShouldContain(p => p.Contains("channel 0 has more than one track"));
        problems.ShouldContain(p => p.Contains("A: instrument"));
        problems.ShouldContain(p => p.Contains("A: note 60 with velocity 0"));
        problems.ShouldContain(p => p.Contains("B: a note at tick 3000 is outside the piece"));
    }

    [Theory]
    [InlineData(0, new byte[] { 0x00 })]
    [InlineData(127, new byte[] { 0x7F })]
    [InlineData(128, new byte[] { 0x81, 0x00 })]
    [InlineData(0x3FFF, new byte[] { 0xFF, 0x7F })]
    [InlineData(0x200000, new byte[] { 0x81, 0x80, 0x80, 0x00 })]
    public void NumbersAreWrittenAsTheMidiSpecificationsVariableLengthQuantities(int value, byte[] bytes) =>
        MidiWriter.VariableLength(value).ShouldBe(bytes);

    [Fact]
    public void TheMidiFileHasAHeaderAConductorTrackAndEveryNote()
    {
        var score = new ProgramScoreWriter().Write(new ScoreBrief("playful pop", 20, 120, 5));

        var file = MidiWriter.Write(score);
        var (tracks, noteOns, tempo) = Read(file);

        file.AsSpan(0, 4).ToArray().ShouldBe("MThd"u8.ToArray());
        ((file[10] << 8) | file[11]).ShouldBe(score.Tracks.Count + 1, "a conductor track and one per instrument");
        ((file[12] << 8) | file[13]).ShouldBe(ScoreRules.TicksPerQuarter);
        tracks.ShouldBe(score.Tracks.Count + 1);
        noteOns.ShouldBe(score.Tracks.Sum(t => t.Notes.Count));
        tempo.ShouldBe(60_000_000 / 120);
    }

    [Fact]
    public async Task TheGeneratorWritesTheMidiFileHasItPlayedAndCostsNothing()
    {
        using var paths = new TempPaths();
        var renderer = new FakeRenderer { Installed = true };
        var generator = new ScoreMusicGenerator(new ProgramScoreWriter(), renderer, NullLogger<ScoreMusicGenerator>.Instance);
        var folder = Path.Combine(paths.WorkDirectory, "piece");

        var made = await generator.GenerateAsync(Request(folder), CancellationToken.None);

        made.Usd.ShouldBe(0);
        made.Exact.ShouldBeTrue();
        made.File.ShouldBe(Path.Combine(folder, ScoreMusicGenerator.WavName));
        renderer.Midi.ShouldBe(Path.Combine(folder, ScoreMusicGenerator.MidiName));
        File.ReadAllBytes(renderer.Midi!).AsSpan(0, 4).ToArray().ShouldBe("MThd"u8.ToArray());
        generator.Handles(Model).ShouldBeTrue();
        generator.Handles(new CatalogModel { Provider = ModelProviders.Local, Id = "ace-step-1.5", Capabilities = ModelCapability.Music }).ShouldBeFalse();
        generator.Ready(Model).ShouldBeTrue();
    }

    [Fact]
    public async Task WithoutTheSynthesizerNothingIsWrittenAndTheReasonIsMissing()
    {
        using var paths = new TempPaths();
        var renderer = new FakeRenderer { Installed = false };
        var generator = new ScoreMusicGenerator(new ProgramScoreWriter(), renderer, NullLogger<ScoreMusicGenerator>.Instance);
        var folder = Path.Combine(paths.WorkDirectory, "piece");

        var error = await Should.ThrowAsync<MusicGenerationException>(() => generator.GenerateAsync(Request(folder), CancellationToken.None));

        error.Code.ShouldBe(MusicFailure.Missing);
        generator.Ready(Model).ShouldBeFalse();
        renderer.Midi.ShouldBeNull();
        Directory.Exists(folder).ShouldBeFalse();
    }

    [Fact]
    public void TheCatalogOffersItAsAFreeLocalModelThatNeedsNoCard()
    {
        var entry = new EmbeddedModelCatalog().Find(ModelProviders.Local, ScoreRules.ModelId).ShouldNotBeNull();

        entry.Capabilities.HasFlag(ModelCapability.Music).ShouldBeTrue();
        entry.Requires.ShouldBe([TMConcept.Core.Components.ComponentCodes.Node, TMConcept.Core.Components.ComponentCodes.MusicSynth,
            TMConcept.Core.Components.ComponentCodes.MusicSoundFont]);
        entry.MinVideoMemoryGb.ShouldBe(0);
        entry.LicenseUse.ShouldBe(LicenseUse.Commercial);
        LocalMusicSupport.NeedsCard(entry).ShouldBeFalse("no graphics card lock, no \"NVIDIA only\" label");
        MusicPricing.Usd(entry, 60).ShouldBe(0);
        TMConcept.Core.DiskSpace.ComponentUsage.FixedNeeds.ShouldContain(TMConcept.Core.Components.ComponentCodes.MusicSynth);
        TMConcept.Core.DiskSpace.ComponentUsage.FixedNeeds.ShouldContain(TMConcept.Core.Components.ComponentCodes.MusicSoundFont);
    }

    private static MusicGenerationRequest Request(string folder) =>
        new(new ResolvedModel(Model, new ApiKey { Provider = ModelProviders.Local }, string.Empty), "calm cinematic", 15, 90, true, folder, 11);

    /// <summary>The tracks, note starts and tempo of a MIDI file, read the way the specification lays it out.</summary>
    private static (int Tracks, int NoteOns, int Tempo) Read(byte[] file)
    {
        var position = 14;
        int tracks = 0, noteOns = 0, tempo = 0;
        while (position < file.Length)
        {
            Encoding().GetString(file, position, 4).ShouldBe("MTrk");
            var length = (file[position + 4] << 24) | (file[position + 5] << 16) | (file[position + 6] << 8) | file[position + 7];
            var at = position + 8;
            var end = at + length;
            while (at < end)
            {
                while ((file[at++] & 0x80) != 0)
                {
                }

                var status = file[at++];
                if (status == 0xFF)
                {
                    var type = file[at++];
                    var size = file[at++];
                    if (type == 0x51)
                    {
                        tempo = (file[at] << 16) | (file[at + 1] << 8) | file[at + 2];
                    }

                    at += size;
                }
                else
                {
                    noteOns += (status & 0xF0) == 0x90 ? 1 : 0;
                    at += (status & 0xF0) is 0xC0 or 0xD0 ? 1 : 2;
                }
            }

            tracks++;
            position = end;
        }

        return (tracks, noteOns, tempo);
    }

    private static System.Text.Encoding Encoding() => System.Text.Encoding.ASCII;

    private sealed class FakeRenderer : IMidiRenderer
    {
        public bool Installed { get; init; }

        public string? Midi { get; private set; }

        public bool Ready => Installed;

        public Task RenderAsync(string midiFile, string wavFile, CancellationToken cancellationToken)
        {
            Midi = midiFile;
            File.WriteAllBytes(wavFile, [1, 2, 3]);
            return Task.CompletedTask;
        }
    }
}
