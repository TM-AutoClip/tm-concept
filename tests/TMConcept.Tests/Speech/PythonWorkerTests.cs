using System.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using TMConcept.Core.Hardware;
using TMConcept.Core.Models;
using TMConcept.Core.Speech;
using TMConcept.Speech.Python;
using TMConcept.Tests.Core;
using TMConcept.Tests.Desktop;
using TMConcept.Tests.Engines;
using TMConcept.Tests.Storage;

namespace TMConcept.Tests.Speech;

/// <summary>
/// The Python worker with a real Python interpreter from the PATH (skipped without one) and a FAKE adapter that writes a short tone: no
/// PyTorch, no model, no network. The shipped worker script, its JSON lines, loading once per voice, the fallback to the processor, failures,
/// a cancel, and the graphics card lease that ends the worker when another model needs the card.
/// </summary>
public sealed class PythonWorkerTests
{
    private const long GiB = 1024L * 1024 * 1024;

    private const string FakeAdapter = """
        import os
        import time

        from tmc_common import sentences, write_wav


        class Fake:
            def __init__(self, model, device, options):
                if options.get("fail") == "1":
                    raise RuntimeError("cannot load " + os.path.basename(model))
                self.device = device
                self.note = ""
                self.sample_rate = 16000
                self.offline = os.environ.get("HF_HUB_OFFLINE", "")

            def synth(self, text, language, voice, reference, speed, out):
                if voice == "slow":
                    time.sleep(60)
                if voice == "broken":
                    raise ValueError("no such voice")
                if voice == "offline" and self.offline != "1":
                    raise ValueError("not offline")
                return write_wav(out, [0.1] * int(0.25 * len(sentences(text, 40)) * self.sample_rate), self.sample_rate)


        def create(model, device, options):
            return Fake(model, device, options)
        """;

    [Fact]
    public async Task TheWorkerLoadsOnceAndSpeaksRequestsOneAfterAnother()
    {
        using var setup = await Setup.CreateAsync();
        using var guard = ChildProcessTests.Guard();
        using var client = new PythonWorkerClient(guard, NullLogger<PythonWorkerClient>.Instance);
        var start = setup.Start("fake-voice|cpu");

        var first = await client.SpeakAsync(start, setup.Synth("Bir. İki.", "offline"), TestContext.Current.CancellationToken);
        first.Ok.ShouldBeTrue(first.Error);
        first.SampleRate.ShouldBe(16000);
        WaveFile.DurationMs(setup.Output).ShouldBe(500, "two sentences of a quarter second");
        client.RunningKey.ShouldBe("fake-voice|cpu");
        var loaded = client.Loaded;
        loaded.ShouldNotBeNull().Device.ShouldBe("cpu");

        var second = await client.SpeakAsync(start, setup.Synth("Üç.", string.Empty), TestContext.Current.CancellationToken);
        second.Ok.ShouldBeTrue();
        client.Loaded.ShouldBeSameAs(loaded, "the same worker keeps its model: no second load");

        await client.StopAsync("other-voice", TestContext.Current.CancellationToken);
        client.RunningKey.ShouldBe("fake-voice|cpu", "only the voice's own unload ends its worker");
        await client.StopAsync("fake-voice", TestContext.Current.CancellationToken);
        client.RunningKey.ShouldBeEmpty();
    }

    [Fact]
    public async Task AskedForTheCardWithoutPyTorchItRunsOnTheProcessorAndSaysWhy()
    {
        using var setup = await Setup.CreateAsync();
        using var guard = ChildProcessTests.Guard();
        using var client = new PythonWorkerClient(guard, NullLogger<PythonWorkerClient>.Instance);

        var reply = await client.SpeakAsync(setup.Start("fake-voice|cuda", "cuda"), setup.Synth("Merhaba.", string.Empty), TestContext.Current.CancellationToken);

        reply.Ok.ShouldBeTrue(reply.Error);
        client.Loaded!.Device.ShouldBe("cpu");
        client.Loaded.Note.ShouldContain("torch");
    }

    [Fact]
    public async Task FailuresComeBackAsAnswersAndAFailedLoadEndsTheWorker()
    {
        using var setup = await Setup.CreateAsync();
        using var guard = ChildProcessTests.Guard();
        using var client = new PythonWorkerClient(guard, NullLogger<PythonWorkerClient>.Instance);

        var broken = await client.SpeakAsync(setup.Start("fake-voice|cpu"), setup.Synth("Merhaba.", "broken"), TestContext.Current.CancellationToken);
        broken.Ok.ShouldBeFalse();
        broken.Code.ShouldBe("engine");
        broken.Error.ShouldContain("no such voice");
        client.RunningKey.ShouldNotBeEmpty("a failed scene does not end the worker");

        var start = setup.Start("fake-voice|cpu") with { Key = "failing|cpu" };
        var load = start.Load with { Options = new Dictionary<string, string> { ["fail"] = "1" } };
        var failed = await client.SpeakAsync(start with { Load = load }, setup.Synth("Merhaba.", string.Empty), TestContext.Current.CancellationToken);
        failed.Ok.ShouldBeFalse();
        failed.Code.ShouldBe("model");
        failed.Error.ShouldContain("cannot load");
        client.RunningKey.ShouldBeEmpty("a model that does not load leaves no worker behind");
    }

    [Fact]
    public async Task ACancelKillsTheWorkerAtOnceAndRemovesTheHalfWrittenFile()
    {
        using var setup = await Setup.CreateAsync();
        using var guard = ChildProcessTests.Guard();
        using var client = new PythonWorkerClient(guard, NullLogger<PythonWorkerClient>.Instance);
        await client.SpeakAsync(setup.Start("fake-voice|cpu"), setup.Synth("Bir.", string.Empty), TestContext.Current.CancellationToken);
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cancel.CancelAfter(TimeSpan.FromMilliseconds(500));
        var clock = Stopwatch.StartNew();

        await Should.ThrowAsync<OperationCanceledException>(() => client.SpeakAsync(setup.Start("fake-voice|cpu"), setup.Synth("Bir.", "slow"), cancel.Token));

        clock.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(20), "the stand-in would sleep a minute");
        client.RunningKey.ShouldBeEmpty();
        File.Exists(setup.Output).ShouldBeFalse();
    }

    [Fact]
    public async Task TheLeaseOfAnotherCardModelEndsThePythonVoicesWorker()
    {
        using var setup = await Setup.CreateAsync();
        using var guard = ChildProcessTests.Guard();
        using var client = new PythonWorkerClient(guard, NullLogger<PythonWorkerClient>.Instance);
        var nvidia = new FakeGpu(new GpuReport([new GpuAdapter(GpuVendor.Nvidia, "RTX", 12 * GiB)], true, true));
        var engine = Engine(setup.Paths, client, nvidia, PythonVoices.XttsV2);
        var source = new PythonModelSource([engine]);
        var coordinator = new GpuCoordinator(new ModelDirectory([source]), new FakeVramProbe(new VramReading(12 * GiB, 11 * GiB, true, "test")),
            TimeProvider.System, new GpuCoordinatorOptions(TimeSpan.FromMilliseconds(1), TimeSpan.FromSeconds(5)), NullLogger<GpuCoordinator>.Instance);
        var xtts = new CatalogModel { Provider = ModelProviders.Local, Id = SpeechEngines.XttsV2, Capabilities = ModelCapability.Speech, MinVideoMemoryGb = 4 };

        await using (var lease = await coordinator.AcquireAsync(xtts, TestContext.Current.CancellationToken))
        {
            lease.OnCard.ShouldBeTrue("on an NVIDIA card the voice holds the card while it speaks");
            lease.Model.ShouldBeOfType<PythonLocalModel>().Device.ShouldBe(ModelDevice.Gpu);

            // The voice's worker (played by the fake adapter under the voice's own key).
            (await client.SpeakAsync(setup.Start(SpeechEngines.XttsV2 + "|cuda"), setup.Synth("Bir.", string.Empty), TestContext.Current.CancellationToken))
                .Ok.ShouldBeTrue();
        }

        client.RunningKey.ShouldNotBeEmpty("the voice stays loaded after its lease until another model needs the card");
        var image = new FakeLocalModel("z-image");
        await using (await coordinator.AcquireAsync(image, null, TestContext.Current.CancellationToken))
        {
            client.RunningKey.ShouldBeEmpty("the image model's lease unloaded the voice: its worker ended and freed the card");
        }
    }

    [Fact]
    public async Task EveryShippedScriptIsValidPython()
    {
        using var setup = await Setup.CreateAsync();
        var scripts = PythonFiles.Scripts.Where(s => s.EndsWith(".py", StringComparison.Ordinal)).Select(s => Path.Combine(setup.Layout.ScriptsFolder, s)).ToList();
        var info = new ProcessStartInfo(setup.Python) { RedirectStandardError = true, RedirectStandardOutput = true, UseShellExecute = false };
        foreach (var argument in (string[])["-I", "-c", "import ast, sys\nfor f in sys.argv[1:]:\n    ast.parse(open(f, encoding='utf-8').read(), f)"])
        {
            info.ArgumentList.Add(argument);
        }

        scripts.ForEach(info.ArgumentList.Add);
        using var process = Process.Start(info)!;
        var errors = await process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        await process.WaitForExitAsync(TestContext.Current.CancellationToken);
        process.ExitCode.ShouldBe(0, errors);
    }

    internal static PythonSpeechEngine Engine(TempPaths paths, PythonWorkerClient client, IGpuInfo gpu, PythonVoice voice)
    {
        var layout = new PythonLayout(paths);
        return new PythonSpeechEngine(voice, layout, new PythonInstaller(layout, null!, null!, gpu, NullLogger<PythonInstaller>.Instance),
            new PythonModelStore(layout, null!, NullLogger<PythonModelStore>.Instance), client,
            new VoiceSampleLibrary(null!, null!, paths, NullLogger<VoiceSampleLibrary>.Instance), gpu, NullLogger<PythonSpeechEngine>.Instance);
    }

    /// <summary>A real Python from the PATH (not the Windows Store stub), the shipped scripts plus the fake adapter in a temp tools folder.</summary>
    private sealed class Setup : IDisposable
    {
        private Setup(string python, TempPaths paths)
        {
            Python = python;
            Paths = paths;
            Layout = new PythonLayout(paths);
        }

        public string Python { get; }

        public TempPaths Paths { get; }

        public PythonLayout Layout { get; }

        public string Output => Path.Combine(Paths.WorkDirectory, "scene.wav");

        public static async Task<Setup> CreateAsync()
        {
            var python = FindPython();
            Assert.SkipWhen(python is null, "No Python on the PATH.");
            var setup = new Setup(python!, new TempPaths());
            await PythonFiles.WriteScriptsAsync(setup.Layout.ScriptsFolder, TestContext.Current.CancellationToken);
            await File.WriteAllTextAsync(Path.Combine(setup.Layout.ScriptsFolder, "adapter_fake.py"), FakeAdapter, TestContext.Current.CancellationToken);
            Directory.CreateDirectory(Path.Combine(setup.Layout.Root, "models", "fake"));
            return setup;
        }

        public PythonWorkerStart Start(string key, string device = "cpu") => new(key, Python,
            ["-I", "-u", Path.Combine(Layout.ScriptsFolder, PythonFiles.Worker)], Layout.ScriptsFolder,
            PythonSpeechEngine.WorkerEnvironment(PythonVoices.MmsTurkish, Layout, device == "cuda", 1),
            new PythonWorkerRequest("load") { Adapter = "fake", Model = Path.Combine(Layout.Root, "models", "fake"), Device = device, NeedMb = 0, Threads = 1 });

        public PythonWorkerRequest Synth(string text, string voice) =>
            new("synth") { Text = text, Language = "tr", Voice = voice, Speed = 1, Out = Output };

        public void Dispose() => Paths.Dispose();

        private static string? FindPython()
        {
            string[] names = OperatingSystem.IsWindows() ? ["python.exe"] : ["python3", "python"];
            foreach (var folder in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                if (folder.Contains("WindowsApps", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                foreach (var name in names)
                {
                    var candidate = Path.Combine(folder, name);
                    if (File.Exists(candidate))
                    {
                        return candidate;
                    }
                }
            }

            return null;
        }
    }
}
