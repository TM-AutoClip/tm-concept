using System.IO.Compression;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using TMConcept.Core.Components;
using TMConcept.Core.Keys;
using TMConcept.Core.Models;
using TMConcept.Core.Music;
using TMConcept.Core.Processes;
using TMConcept.Core.Tools;
using TMConcept.Engines.Tools;
using TMConcept.Speech.Music;
using TMConcept.Speech.Python;
using TMConcept.Tests.Storage;

namespace TMConcept.Tests.Speech;

/// <summary>
/// The local music model (0.1.46, C5) through fakes only: nothing is downloaded, uv and Python never run, no model starts. Its two components
/// (environment, then weights) list their downloads with their sizes and are not there until installed; the environment's install unpacks the
/// pinned source archives, runs <c>uv sync --frozen</c> in ACE-Step's folder with the app's Python and writes rakkase's settings; the weights
/// come from rakkase's pinned list, each file by its SHA-256, only from Hugging Face and only inside <c>checkpoints/</c>; a piece is one
/// rakkase run whose WAV is the result, and a failed run is a music failure.
/// </summary>
public sealed class RakkaseInstallTests : IDisposable
{
    private readonly TempPaths _paths = new();
    private readonly RakkaseLayout _layout;
    private readonly PythonLayout _python;

    public RakkaseInstallTests()
    {
        _layout = new RakkaseLayout(_paths);
        _python = new PythonLayout(_paths);
    }

    public void Dispose() => _paths.Dispose();

    [Fact]
    public void ThePinsAreGitHubArchivesAndHuggingFaceWeightsFromAllowedHosts()
    {
        foreach (var archive in new[] { RakkasePins.Rakkase, RakkasePins.Ace })
        {
            archive.Sha256.Length.ShouldBe(64);
            ToolManifest.AllowedHosts.ShouldContain(archive.Url.Host);
            archive.Url.ToString().ShouldEndWith(archive.Root[(archive.Root.LastIndexOf('-') + 1)..], Case.Sensitive, "the archive of the pinned commit");
        }

        ToolManifest.AllowedHosts.ShouldContain(RakkasePins.WeightsHost);
        ToolManifest.Libraries.ShouldContain(ToolKind.MusicCode);
        ToolManifest.Libraries.ShouldContain(ToolKind.MusicWeights);
    }

    [Fact]
    public void NothingIsThereUntilInstalledAndTheDownloadsShowTheirSizes()
    {
        var (environment, weights) = Components(new FakeRunner(_layout), new FakeDownloader());

        environment.Code.ShouldBe(ComponentCodes.MusicEnvironment);
        environment.DependsOn.ShouldBe([ComponentCodes.Python]);
        weights.Code.ShouldBe(ComponentCodes.MusicModel);
        weights.DependsOn.ShouldBe([ComponentCodes.MusicEnvironment], "the weights are a component of their own, after the environment");
        environment.Ready.ShouldBeFalse();
        weights.Ready.ShouldBeFalse();
        var all = environment.Downloads().Concat(weights.Downloads()).ToList();
        all.Sum(d => d.ApproximateBytes).ShouldBe(MusicGenerationRules.LocalInstallBytes, "about 23 GB in all, shown before the yes");
        weights.Downloads().ShouldHaveSingleItem().Host.ShouldBe("huggingface.co");
    }

    [Fact]
    public async Task TheEnvironmentIsUnpackedSyncedWithTheAppsPythonAndSetUpForRakkase()
    {
        var runner = new FakeRunner(_layout);
        var downloader = new FakeDownloader();
        var installer = Installer(runner, downloader);
        InstallPython();

        await installer.InstallEnvironmentAsync(null, CancellationToken.None);

        downloader.Asked.Select(a => a.Url.Host).ShouldBe([RakkasePins.ArchiveHost, RakkasePins.ArchiveHost]);
        File.Exists(_layout.Script).ShouldBeTrue();
        File.Exists(Path.Combine(_layout.AceFolder, "pyproject.toml")).ShouldBeTrue();
        var sync = runner.Runs.ShouldHaveSingleItem();
        sync.Arguments.Take(2).ShouldBe(["sync", "--frozen"], "exactly ACE-Step's lock");
        sync.Arguments.ShouldContain("--no-python-downloads");
        sync.Arguments[sync.Arguments.ToList().IndexOf("--python") + 1].ShouldBe(PythonInterpreter());
        sync.WorkingDirectory.ShouldBe(_layout.AceFolder);
        sync.Environment["UV_CACHE_DIR"].ShouldBe(_python.CacheFolder);
        var settings = JsonNode.Parse(await File.ReadAllTextAsync(_layout.Settings, TestContext.Current.CancellationToken))!;
        settings["ace_kok"]!.GetValue<string>().ShouldBe(_layout.AceFolder.Replace('\\', '/'));
        settings["kilit"]!.GetValue<bool>().ShouldBeFalse("the app holds the graphics card's lock");
        installer.EnvironmentReady.ShouldBeTrue();
        Directory.Exists(_layout.Downloads).ShouldBeFalse("the archives are removed");
    }

    [Fact]
    public async Task AFailedSyncIsAnInstallFailure()
    {
        var installer = Installer(new FakeRunner(_layout) { Fail = true }, new FakeDownloader());
        InstallPython();

        var error = await Should.ThrowAsync<ToolInstallException>(() => installer.InstallEnvironmentAsync(null, CancellationToken.None));

        error.Code.ShouldBe("install");
        installer.EnvironmentReady.ShouldBeFalse();
    }

    [Fact]
    public void TheWeightListIsReadOnlyFromHuggingFaceAndOnlyIntoCheckpoints()
    {
        var list = RakkaseInstaller.ReadWeights(WeightList(("vae/config.json", 10)), _layout.Checkpoints);
        list.ShouldHaveSingleItem().File.ShouldBe(Path.Combine(_layout.Checkpoints, "vae", "config.json"));

        Should.Throw<InvalidDataException>(() => RakkaseInstaller.ReadWeights(WeightList(("../../evil.py", 10)), _layout.Checkpoints));
        Should.Throw<InvalidDataException>(() => RakkaseInstaller.ReadWeights(WeightList(("a.bin", 10), "https://example.com/a.bin"), _layout.Checkpoints));
        Should.Throw<InvalidDataException>(() => RakkaseInstaller.ReadWeights(WeightList(("a.bin", 10), "http://huggingface.co/a.bin"), _layout.Checkpoints));
        Should.Throw<InvalidDataException>(() => RakkaseInstaller.ReadWeights("""{"dosyalar":[]}""", _layout.Checkpoints));
    }

    [Fact]
    public async Task TheWeightsComeFileByFilePinnedAndTheMarkerIsWrittenLast()
    {
        var downloader = new FakeDownloader();
        var (_, weights) = Components(new FakeRunner(_layout), downloader);
        Directory.CreateDirectory(Path.GetDirectoryName(_layout.WeightList)!);
        await File.WriteAllTextAsync(_layout.WeightList, WeightList(("vae/config.json", 425), ("acestep-v15-turbo/model.safetensors", 2048)), TestContext.Current.CancellationToken);

        weights.Downloads().ShouldHaveSingleItem().ApproximateBytes.ShouldBe(2473, "the sizes of the pinned list");
        await weights.InstallAsync(null, CancellationToken.None);

        downloader.Asked.Count.ShouldBe(2);
        downloader.Asked.ShouldAllBe(a => a.Tool == ToolKind.MusicWeights && a.Sha256.Length == 64 && a.Url.Host == "huggingface.co");
        weights.Ready.ShouldBeTrue();
        weights.Downloads().ShouldBeEmpty();
        File.ReadAllText(_layout.WeightsMarker).ShouldBe(RakkasePins.WeightsRevision);
    }

    [Fact]
    public async Task APieceIsOneRakkaseRunAndItsWavIsTheResult()
    {
        var runner = new FakeRunner(_layout) { WritesWav = true };
        var generator = await ReadyGeneratorAsync(runner);
        var output = Path.Combine(_paths.WorkDirectory, "music-generated");

        var piece = await generator.GenerateAsync(Request(output), CancellationToken.None);

        var run = runner.Runs.Last();
        run.Program.ShouldBe(_layout.Python);
        run.Arguments.ShouldBe(RakkaseMusicGenerator.Arguments(_layout.Script, Path.Combine(output, "plan.json"), Path.Combine(output, "out")));
        run.Arguments.ShouldContain("--panel-yok");
        run.WorkingDirectory.ShouldBe(_layout.RakkaseFolder);
        run.Environment["HF_HUB_OFFLINE"].ShouldBe("1", "the model never downloads anything by itself");
        var plan = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(output, "plan.json"), TestContext.Current.CancellationToken))!["parcalar"]![0]!;
        plan["caption"]!.GetValue<string>().ShouldBe("calm ambient, no vocals");
        plan["sure"]!.GetValue<int>().ShouldBe(45);
        plan["bpm"]!.GetValue<int>().ShouldBe(72);
        plan.AsObject().ContainsKey("lyrics").ShouldBeFalse("no lyrics = instrumental");
        piece.File.ShouldEndWith(".wav");
        piece.Usd.ShouldBe(0);
        Directory.EnumerateDirectories(_layout.Productions).ShouldBeEmpty("rakkase's own copy is removed");
    }

    [Fact]
    public async Task AFailedRunIsAMusicFailure()
    {
        var runner = new FakeRunner(_layout);
        var generator = await ReadyGeneratorAsync(runner);
        runner.Fail = true;

        var error = await Should.ThrowAsync<MusicGenerationException>(() => generator.GenerateAsync(Request(Path.Combine(_paths.WorkDirectory, "g")), CancellationToken.None));

        error.Code.ShouldBe("process");
    }

    [Fact]
    public async Task ThePieceIsNotMadeWhileTheModelIsNotInstalled()
    {
        var runner = new FakeRunner(_layout);
        var generator = new RakkaseMusicGenerator(Installer(runner, new FakeDownloader()), _layout, runner, NullLogger<RakkaseMusicGenerator>.Instance);
        var model = new CatalogModel { Provider = ModelProviders.Local, Id = RakkaseMusicGenerator.ModelId, Capabilities = ModelCapability.Music };

        generator.Handles(model).ShouldBeTrue();
        generator.Ready(model).ShouldBeFalse();
        await Should.ThrowAsync<MusicGenerationException>(() => generator.GenerateAsync(Request(Path.Combine(_paths.WorkDirectory, "g")), CancellationToken.None));
        runner.Runs.ShouldBeEmpty();
    }

    private async Task<RakkaseMusicGenerator> ReadyGeneratorAsync(FakeRunner runner)
    {
        var downloader = new FakeDownloader();
        var installer = Installer(runner, downloader);
        InstallPython();
        await installer.InstallEnvironmentAsync(null, CancellationToken.None);
        await File.WriteAllTextAsync(_layout.WeightList, WeightList(("vae/config.json", 425)), TestContext.Current.CancellationToken);
        await installer.InstallWeightsAsync(null, CancellationToken.None);
        installer.WeightsReady.ShouldBeTrue();
        return new RakkaseMusicGenerator(installer, _layout, runner, NullLogger<RakkaseMusicGenerator>.Instance);
    }

    private static MusicGenerationRequest Request(string output) => new(
        new ResolvedModel(new CatalogModel { Provider = ModelProviders.Local, Id = RakkaseMusicGenerator.ModelId }, new ApiKey { Provider = ModelProviders.Local },
            string.Empty), "calm ambient, no vocals", 45, 72, true, output, 7);

    private RakkaseInstaller Installer(FakeRunner runner, FakeDownloader downloader) => new(_layout,
        new PythonInstaller(_python, new UvTools(Path.Combine(_paths.ToolsDirectory, "uv.exe")), runner, new TMConcept.Tests.Desktop.FakeGpu(0), NullLogger<PythonInstaller>.Instance),
        _python, new UvTools(Path.Combine(_paths.ToolsDirectory, "uv.exe")), runner, downloader, NullLogger<RakkaseInstaller>.Instance);

    private (IComponent Environment, IComponent Weights) Components(FakeRunner runner, FakeDownloader downloader)
    {
        var source = new MusicComponentSource(Installer(runner, downloader), _layout);
        return (source.Components.Single(c => c.Code == ComponentCodes.MusicEnvironment), source.Components.Single(c => c.Code == ComponentCodes.MusicModel));
    }

    /// <summary>The app's Python, as its marker names it.</summary>
    private void InstallPython()
    {
        var interpreter = PythonInterpreter();
        Directory.CreateDirectory(Path.GetDirectoryName(interpreter)!);
        File.WriteAllText(interpreter, "fake");
        File.WriteAllText(_python.PythonMarker, interpreter);
    }

    private string PythonInterpreter() => Path.Combine(_python.PythonFolder, "cpython-3.11.18", "python.exe");

    private static string WeightList(params (string Target, long Bytes)[] files) => WeightList(files[0], null, files.Skip(1).ToArray());

    private static string WeightList((string Target, long Bytes) first, string? url, params (string Target, long Bytes)[] more)
    {
        var list = new JsonArray();
        foreach (var (target, bytes) in new[] { first }.Concat(more))
        {
            list.Add(new JsonObject
            {
                ["hedef"] = target,
                ["url"] = url ?? "https://huggingface.co/ACE-Step/Ace-Step1.5/resolve/" + RakkasePins.WeightsRevision + "/" + target,
                ["sha256"] = new string('a', 64),
                ["boyut"] = bytes,
            });
        }

        return new JsonObject { ["dosyalar"] = list }.ToJsonString();
    }

    /// <summary>Writes a source archive with its top folder and entry file, or a weight file of its pinned size (no network).</summary>
    private sealed class FakeDownloader : IArchiveDownloader
    {
        public List<ToolArchive> Asked { get; } = [];

        public Task DownloadAsync(ToolArchive archive, string file, IProgress<ToolProgress>? progress, CancellationToken cancellationToken)
        {
            Asked.Add(archive);
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            if (archive.Tool == ToolKind.MusicCode)
            {
                using var zip = ZipFile.Open(file, ZipArchiveMode.Create);
                using (var writer = new StreamWriter(zip.CreateEntry(archive.Root + "/" + archive.ExecutablePath).Open()))
                {
                    writer.Write("# fake");
                }

                if (archive.ExecutablePath == "rakkase.py")
                {
                    using var list = new StreamWriter(zip.CreateEntry(archive.Root + "/scripts/modeller.json").Open());
                    list.Write(WeightList(("vae/config.json", 425)));
                }
            }
            else
            {
                using var stream = File.Create(file);
                stream.SetLength(archive.ApproximateBytes);
            }

            return Task.CompletedTask;
        }
    }

    /// <summary>uv sync makes the environment's python; a rakkase run writes a WAV under its output and a copy in its archive.</summary>
    private sealed class FakeRunner(RakkaseLayout layout) : IProcessRunner
    {
        public List<ProcessStart> Runs { get; } = [];

        public bool Fail { get; set; }

        public bool WritesWav { get; init; }

        public Task<ProcessResult> RunAsync(ProcessStart start, Action<string>? onLine, CancellationToken cancellationToken)
        {
            Runs.Add(start);
            if (Fail)
            {
                return Task.FromResult(new ProcessResult(1, "RK-E-NAN: the piece is broken"));
            }

            if (start.Arguments.FirstOrDefault() == "sync")
            {
                Directory.CreateDirectory(Path.GetDirectoryName(layout.Python)!);
                File.WriteAllText(layout.Python, "fake");
            }
            else if (WritesWav)
            {
                var output = start.Arguments[start.Arguments.ToList().IndexOf("--cikti") + 1];
                Directory.CreateDirectory(Path.Combine(output, RakkasePins.RunName));
                File.WriteAllBytes(Path.Combine(output, RakkasePins.RunName, "tmc.wav"), [1, 2, 3]);
                Directory.CreateDirectory(Path.Combine(layout.Productions, "2026-11-24_06-00-00_tmc"));
            }

            return Task.FromResult(new ProcessResult(0, string.Empty));
        }
    }

    private sealed class UvTools(string uv) : IToolLocator
    {
        public ToolStatus Status(ToolKind tool) =>
            tool == ToolKind.Uv ? new ToolStatus(tool, true, ToolManifest.UvVersion, uv, "app") : new ToolStatus(tool, false, string.Empty, string.Empty, string.Empty);
    }
}
