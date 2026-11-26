using System.Security.Cryptography;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using TMConcept.Core.Keys;
using TMConcept.Core.Models;
using TMConcept.Core.Music;
using TMConcept.Core.Processes;
using TMConcept.Core.Tools;
using TMConcept.Engines.Music;
using TMConcept.Engines.Processes;
using TMConcept.Engines.Tools;
using TMConcept.Tests.Storage;

namespace TMConcept.Tests.Engines;

/// <summary>
/// 0.1.55, "Program composes" for real, on the processor: only when <c>TMC_SOUNDFONT</c> names a copy of the pinned MuseScore_General.sf3 (its
/// SHA-256 is checked first) and a Node.js is on the PATH. The SoundFont is copied into a temporary tools folder (never the user's data folder),
/// the synthesizer's pinned npm packages are installed the way the app installs them (<c>npm ci --ignore-scripts</c>, about 1.4 MB from
/// registry.npmjs.org), and a composed piece is played into a WAV file that is checked: 16-bit stereo 44.1 kHz, as long as the score plus its
/// tail, and not silent. When <c>TMC_MUSIC_KEEP</c> names a folder, the pieces are copied there to be listened to. Skipped otherwise. No graphics
/// card, no model.
/// </summary>
public sealed class MusicSynthSmokeTests
{
    [Theory]
    [InlineData("antik Mısır'da disko, gizemli", 20, "egypt-disco")]
    [InlineData("hüzünlü piyano, yavaş", 30, "sad-piano")]
    [InlineData("epic orchestral battle", 30, "epic-orchestral")]
    public async Task AComposedPieceIsPlayedIntoARealWavFileOnTheProcessor(string prompt, int length, string name)
    {
        var source = Environment.GetEnvironmentVariable("TMC_SOUNDFONT");
        if (string.IsNullOrEmpty(source) || !File.Exists(source))
        {
            Assert.Skip("TMC_SOUNDFONT does not name a copy of " + SoundFontPins.FileName + ".");
        }

        using var paths = new TempPaths();
        IProcessRunner runner = new ProcessRunner(ChildProcessTests.Guard(), NullLogger<ProcessRunner>.Instance);
        IToolPlatform platform = OperatingSystem.IsWindows() ? new WindowsToolPlatform(runner) : new LinuxToolPlatform(runner);
        var downloader = new ToolDownloader(DownloadTransport.Network, NullLogger<ToolDownloader>.Instance);
        var tools = new ToolInstaller(paths, platform, downloader, NullLogger<ToolInstaller>.Instance);
        var node = tools.FindProgram("node");
        Assert.SkipUnless(node is not null, "No Node.js on the PATH.");
        tools.UseSystemNode(node);

        Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(source, TestContext.Current.CancellationToken)))
            .ShouldBe(SoundFontPins.Archive.Sha256, "the pinned SoundFont");
        var store = new SoundFontStore(paths, downloader, NullLogger<SoundFontStore>.Instance);
        Directory.CreateDirectory(store.Folder);
        File.Copy(source, store.File);
        var renderer = new SpessaSynthRenderer(paths, tools, runner, store, NullLogger<SpessaSynthRenderer>.Instance);
        await renderer.Package.InstallAsync(null, TestContext.Current.CancellationToken);
        renderer.Ready.ShouldBeTrue();

        var model = new CatalogModel { Provider = ModelProviders.Local, Id = ScoreRules.ModelId, Name = "Program composes", Capabilities = ModelCapability.Music };
        var generator = new ScoreMusicGenerator(new ProgramScoreWriter(), renderer, NullLogger<ScoreMusicGenerator>.Instance);
        var folder = Path.Combine(paths.WorkDirectory, "piece");
        var request = new MusicGenerationRequest(new ResolvedModel(model, new ApiKey { Provider = ModelProviders.Local }, string.Empty),
            prompt, length, null, true, folder, 42);
        var score = new ProgramScoreWriter().Write(new ScoreBrief(request.Prompt, request.Seconds, request.Bpm, request.Seed));

        var made = await generator.GenerateAsync(request, TestContext.Current.CancellationToken);

        var wav = await File.ReadAllBytesAsync(made.File, TestContext.Current.CancellationToken);
        System.Text.Encoding.ASCII.GetString(wav, 0, 4).ShouldBe("RIFF");
        System.Text.Encoding.ASCII.GetString(wav, 8, 4).ShouldBe("WAVE");
        var (channels, rate, bits, data) = Format(wav);
        (channels, rate, bits).ShouldBe((2, 44_100, 16));
        var seconds = data.Length / (double)(channels * rate * (bits / 8));
        seconds.ShouldBe(score.Seconds + double.Parse(MusicSynthRules.TailSeconds, System.Globalization.CultureInfo.InvariantCulture), 0.05);
        var loudest = 0;
        for (var i = 0; i + 1 < data.Length; i += 2)
        {
            loudest = Math.Max(loudest, Math.Abs((int)BitConverter.ToInt16(data, i)));
        }

        loudest.ShouldBeGreaterThan(short.MaxValue / 4, "the instruments are heard (the file is normalised)");
        if (Environment.GetEnvironmentVariable("TMC_MUSIC_KEEP") is { Length: > 0 } keep && Directory.Exists(keep))
        {
            File.Copy(made.File, Path.Combine(keep, name + ".wav"), overwrite: true);
            File.Copy(Path.Combine(folder, ScoreMusicGenerator.MidiName), Path.Combine(keep, name + ".mid"), overwrite: true);
        }
    }

    /// <summary>The format and the samples of a PCM WAV file (its chunks walked, an INFO chunk skipped).</summary>
    private static (int Channels, int Rate, int Bits, byte[] Data) Format(byte[] wav)
    {
        int channels = 0, rate = 0, bits = 0;
        var at = 12;
        while (at + 8 <= wav.Length)
        {
            var id = System.Text.Encoding.ASCII.GetString(wav, at, 4);
            var size = BitConverter.ToInt32(wav, at + 4);
            if (id == "fmt ")
            {
                channels = BitConverter.ToInt16(wav, at + 10);
                rate = BitConverter.ToInt32(wav, at + 12);
                bits = BitConverter.ToInt16(wav, at + 22);
            }
            else if (id == "data")
            {
                return (channels, rate, bits, wav.AsSpan(at + 8, Math.Min(size, wav.Length - at - 8)).ToArray());
            }

            at += 8 + size + (size % 2);
        }

        return (channels, rate, bits, []);
    }
}
