using System.Net;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using TMConcept.Core.Keys;
using TMConcept.Core.Models;
using TMConcept.Core.Speech;
using TMConcept.Providers.Catalog;
using TMConcept.Providers.Common;
using TMConcept.Providers.OpenAICompatible;
using TMConcept.Providers.Speech;
using TMConcept.Tests.Storage;

namespace TMConcept.Tests.Providers;

public sealed class SpeechProviderTests
{
    private const string Key = "fake-test-key-speech-0123456789";

    private static byte[] Wav(int ms)
    {
        const int rate = 24000;
        var bytes = rate * 2 * ms / 1000;
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write("RIFF"u8);
        writer.Write(36 + bytes);
        writer.Write("WAVE"u8);
        writer.Write("fmt "u8);
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(rate);
        writer.Write(rate * 2);
        writer.Write((short)2);
        writer.Write((short)16);
        writer.Write("data"u8);
        writer.Write(bytes);
        writer.Write(new byte[bytes]);
        writer.Flush();
        return stream.ToArray();
    }

    [Fact]
    public async Task OpenAISpeechSendsTheKeyOnlyToOpenAIAndReturnsTheWavLength()
    {
        var fake = new FakeHttp();
        fake.Answer(HttpStatusCode.OK, string.Empty, "audio/wav", r => r.Content = new ByteArrayContent(Wav(1500)));
        var http = new ProviderHttp(fake.Transport, NullLogger<ProviderHttp>.Instance);
        var engine = new OpenAISpeechEngine(new OpenAIProvider(http, NullLogger<OpenAIProvider>.Instance), http);
        var model = new EmbeddedModelCatalog().Find("openai", "gpt-4o-mini-tts")!;
        using var paths = new TempPaths();
        var output = Path.Combine(paths.WorkDirectory, "a.wav");

        var clip = await engine.SynthesizeAsync(new SpeechRequest("Hello there.", "en", "coral", 1.0, output,
            new ResolvedModel(model, new ApiKey { Provider = "openai", Secret = Key }, string.Empty)), CancellationToken.None);

        clip.DurationMs.ShouldBe(1500);
        clip.CostUsd.ShouldBeGreaterThan(0);
        var request = fake.Requests.Single();
        request.Uri.ShouldBe(new Uri("https://api.openai.com/v1/audio/speech"));
        request.Headers["Authorization"].ShouldBe("Bearer " + Key);
        var body = JsonNode.Parse(request.Body)!;
        body["response_format"]!.ToString().ShouldBe("wav");
        body["voice"]!.ToString().ShouldBe("coral");
    }

    [Fact]
    public async Task LocalSpeechWhenTheModelIsInstalled()
    {
        // Real Supertonic 3 speech on the processor, in the speech worker process (the app's own program, next to the tests). Runs only when
        // TMCONCEPT_RENDER_DATA points to a data folder with the voice model and the sherpa-onnx libraries installed.
        var data = Environment.GetEnvironmentVariable("TMCONCEPT_RENDER_DATA");
        if (string.IsNullOrEmpty(data))
        {
            Assert.Skip("TMCONCEPT_RENDER_DATA is not set.");
        }

        using var paths = TempPaths.At(data);
        var program = Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "TMConcept.exe" : "TMConcept");
        var tools = new TMConcept.Engines.Tools.ToolInstaller(paths, OperatingSystem.IsWindows() ? new TMConcept.Engines.Tools.WindowsToolPlatform(null!)
            : new TMConcept.Engines.Tools.LinuxToolPlatform(null!), null!, NullLogger<TMConcept.Engines.Tools.ToolInstaller>.Instance);
        var installer = new TMConcept.Speech.Speech.SpeechModelInstaller(paths, null!, null!, NullLogger<TMConcept.Speech.Speech.SpeechModelInstaller>.Instance);
        using TMConcept.Engines.Processes.ChildProcessGuardBase guard = OperatingSystem.IsWindows()
            ? new TMConcept.Engines.Processes.WindowsChildProcessGuard(NullLogger<TMConcept.Engines.Processes.WindowsChildProcessGuard>.Instance)
            : new TMConcept.Engines.Processes.LinuxChildProcessGuard(NullLogger<TMConcept.Engines.Processes.LinuxChildProcessGuard>.Instance);
        using var worker = new TMConcept.Speech.Speech.SpeechWorkerClient(guard, NullLogger<TMConcept.Speech.Speech.SpeechWorkerClient>.Instance)
        {
            ProgramPath = program,
        };
        var engine = new TMConcept.Speech.Speech.SupertonicSpeechEngine(installer, tools, worker, NullLogger<TMConcept.Speech.Speech.SupertonicSpeechEngine>.Instance);
        if (!engine.Ready || !File.Exists(program))
        {
            Assert.Skip("Supertonic or its libraries are not installed in TMCONCEPT_RENDER_DATA, or the app program is not next to the tests.");
        }

        foreach (var language in new[] { "tr", "en" })
        {
            var output = Path.Combine(paths.WorkDirectory, "speech-test-" + language + ".wav");
            var text = language == "tr" ? "Bin yıl ayakta kalan bir şehrin hikâyesi." : "The story of a city that stood for a thousand years.";
            var clip = await engine.SynthesizeAsync(new SpeechRequest(text, language, "3", 1.0, output), TestContext.Current.CancellationToken);
            clip.DurationMs.ShouldBeGreaterThan(1000);
            Math.Abs(WaveFile.DurationMs(output) - clip.DurationMs).ShouldBeLessThan(50);
        }

        worker.Running.ShouldBeTrue();
    }
}
