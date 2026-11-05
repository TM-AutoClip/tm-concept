using System.Text;
using System.Text.Json.Nodes;
using Shouldly;
using TMConcept.Core.Speech;
using TMConcept.Core.Tools;
using TMConcept.Engines.Tools;
using TMConcept.Speech.Speech;
using TMConcept.Tests.Storage;

namespace TMConcept.Tests.Speech;

public sealed class SpeechWorkerTests
{
    [Fact]
    public void AWrittenWaveHasTheRightLengthAndFormat()
    {
        using var paths = new TempPaths();
        var file = Path.Combine(paths.WorkDirectory, "sub", "one-second.wav");
        var samples = Enumerable.Range(0, 24_000).Select(i => (float)Math.Sin(i / 10.0) * 1.5f).ToArray();

        WaveFile.WritePcm16(file, samples, 24_000);

        WaveFile.DurationMs(file).ShouldBe(1000);
        var bytes = File.ReadAllBytes(file);
        bytes.Length.ShouldBe(44 + (24_000 * 2));
        BitConverter.ToInt16(bytes, 22).ShouldBe((short)1);
        BitConverter.ToInt16(bytes, 34).ShouldBe((short)16);
        bytes.Skip(44).Chunk(2).Select(b => BitConverter.ToInt16(b)).Max().ShouldBe(short.MaxValue);
    }

    [Fact]
    public void TheWorkerRefusesWrongArguments()
    {
        using var error = new StringWriter();

        SpeechWorker.Run([SpeechWorker.Argument], new MemoryStream(), new MemoryStream(), error).ShouldBe(2);

        error.ToString().ShouldContain("usage");
    }

    [Fact]
    public void WithoutItsLibrariesTheWorkerAnswersOnceWithTheReasonAndStops()
    {
        using var paths = new TempPaths();
        var request = """{"text":"Merhaba dünya","language":"tr","speaker":0,"speed":1,"steps":8,"outputWav":"x.wav"}""" + "\n" + "{}\n";
        using var input = new MemoryStream(Encoding.UTF8.GetBytes(request));
        using var output = new MemoryStream();
        using var error = new StringWriter();

        var exit = SpeechWorker.Run([SpeechWorker.Argument, Path.Combine(paths.ToolsDirectory, "none"), "2"], input, output, error);

        exit.ShouldBe(3);
        var lines = Encoding.UTF8.GetString(output.ToArray()).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        lines.Length.ShouldBe(1);
        var reply = JsonNode.Parse(lines[0])!;
        reply["ok"]!.GetValue<bool>().ShouldBeFalse();
        reply["code"]!.GetValue<string>().ShouldBe("model");
        reply["error"]!.GetValue<string>().ShouldContain("missing");
        error.ToString().ShouldContain("FileNotFoundException");
    }

    [Fact]
    public void TheSpeechLibrariesArePinnedForEverySupportedComputerAndNeverTakenFromThePath()
    {
        foreach (var rid in new[] { "win-x64", "linux-x64", "linux-arm64" })
        {
            var archive = ToolManifest.Archives.Single(a => a.Tool == ToolKind.SpeechRuntime && a.Rid == rid);
            archive.Version.ShouldBe(ToolManifest.SherpaVersion);
            archive.Url.Host.ShouldBe("github.com");
            archive.Url.AbsolutePath.ShouldStartWith("/k2-fsa/sherpa-onnx/releases/download/v" + ToolManifest.SherpaVersion + "/");
            archive.Sha256.Length.ShouldBe(64);
            archive.ExecutablePath.ShouldEndWith(rid == "win-x64" ? "sherpa-onnx-c-api.dll" : "libsherpa-onnx-c-api.so");
        }

        ToolManifest.Libraries.ShouldContain(ToolKind.SpeechRuntime);
    }
}
