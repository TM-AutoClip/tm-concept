using System.IO.Compression;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using TMConcept.Core.Components;
using TMConcept.Core.Hardware;
using TMConcept.Core.Keys;
using TMConcept.Core.Models;
using TMConcept.Core.Music;
using TMConcept.Core.Processes;
using TMConcept.Core.Tools;
using TMConcept.Engines.Tools;
using TMConcept.Providers.Catalog;
using TMConcept.Speech.Components;
using TMConcept.Speech.Music;
using TMConcept.Speech.Python;
using TMConcept.Tests.Desktop;
using TMConcept.Tests.Storage;

namespace TMConcept.Tests.Speech;

/// <summary>
/// The MusicGen, MAGNeT, AudioLDM 2, DiffRhythm, SongGeneration, YuE2 and YuE 1 families (28 Nov 2026) through fakes only: nothing is downloaded, uv and Python never run, no model
/// starts, no graphics card is used. Every catalog row has its pinned model (Hugging Face at a commit, SHA-256 and size), its non-commercial
/// licence, its card class and its components (the family's card-only environment, MAGNeT's audiocraft source, its own weights); a 6 GB card
/// sees exactly the models that fit it; the shared text encoder is downloaded once; a piece is one run of the family's script whose WAV is the
/// result, and the script's own failure line is the reason.
/// </summary>
public sealed class LocalMusicTests : IDisposable
{
    private const long GiB = 1024L * 1024 * 1024;

    private static readonly LocalMusicFamily[] Families = [.. LocalMusicFamilies.All];

    private readonly TempPaths _paths = new();
    private readonly LocalMusicLayout _layout;
    private readonly PythonLayout _python;

    public LocalMusicTests()
    {
        _layout = new LocalMusicLayout(_paths);
        _python = new PythonLayout(_paths);
    }

    public void Dispose() => _paths.Dispose();

    [Fact]
    public void EveryPinnedModelHasItsCatalogRowWithItsLicenceLengthAndComponents()
    {
        var catalog = new EmbeddedModelCatalog();
        foreach (var family in Families)
        {
            foreach (var variant in family.Variants)
            {
                var model = catalog.Find(ModelProviders.Local, variant.Id).ShouldNotBeNull(variant.Id);
                model.Capabilities.ShouldBe(ModelCapability.Music);
                // YuE 1 is Apache-2.0 (commercial use allowed); every other family's weights are non-commercial.
                model.LicenseUse.ShouldBe(family == LocalMusicFamilies.Yue ? LicenseUse.Commercial : LicenseUse.NonCommercial,
                    variant.Id + ": the orange warning and the one-time question");
                model.License.ShouldBe(Licence(family));
                model.LicenseUrl.ShouldStartWith("https://huggingface.co/");
                model.MusicLyrics.ShouldBe(family.Lyrics, variant.Id + ": the lyrics field only for a model that sings");
                // A fixed length is the catalog's length (MAGNeT, DiffRhythm's base models and 1.0 Full); a model whose length follows the words
                // or the asked length names none, or the asked length would be ignored (MusicPricing.Seconds).
                var fixedLength = family == LocalMusicFamilies.Magnet || family == LocalMusicFamilies.DiffRhythmLegacy
                    || (family == LocalMusicFamilies.DiffRhythm && variant != DiffRhythmModels.Full12);
                model.MusicSeconds.ShouldBe(fixedLength ? variant.MaxSeconds : 0, variant.Id + ": the catalog's length only for a fixed-length model");

                ((int)VramTiers.ForModel(model.MinVideoMemoryGb)).ShouldBe(model.MinVideoMemoryGb, variant.Id + ": a card class");
                LocalMusicSupport.NeedsCard(model).ShouldBeTrue();
                IEnumerable<string> expected = family.Source is not { } archive
                    ? [PythonEnvironmentComponent.CodeOf(family.Environment.Key), ComponentCodes.MusicModelPrefix + variant.Id]
                    : [PythonEnvironmentComponent.CodeOf(family.Environment.Key), ComponentCodes.MusicCodePrefix + LocalMusicComponentSource.SourceName(family, archive),
                        ComponentCodes.MusicModelPrefix + variant.Id];
                model.Requires.ShouldBe(expected.ToList());
            }
        }

        var pinned = Families.SelectMany(f => f.Variants).Select(v => v.Id).ToHashSet();
        pinned.Count.ShouldBe(34, "10 MusicGen, 6 MAGNeT, 3 AudioLDM 2, 4 DiffRhythm, 6 SongGeneration, 2 YuE2, 3 YuE 1");
        catalog.All.Where(m => m.Provider == ModelProviders.Local && m.Capabilities.HasFlag(ModelCapability.Music))
            .Select(m => m.Id).Where(id => id is not RakkaseMusicGenerator.ModelId and not ScoreRules.ModelId).ShouldAllBe(id => pinned.Contains(id));
    }

    [Fact]
    public void ASixGigabyteCardSeesExactlyTheModelsThatFitIt()
    {
        // The developer's RTX 3060 Laptop (6 GB): the card classes come from the official figures where there are any, otherwise from the
        // weights in 16 bits plus the cache and the CUDA context (estimates; the research of 28 Nov 2026).
        var catalog = new EmbeddedModelCatalog();
        var card = LocalMusicSupport.Of(new GpuReport([new GpuAdapter(GpuVendor.Nvidia, "RTX 3060 Laptop", 6 * GiB)], true, true), linux: false);
        card.Tier.ShouldBe(VramTier.Gb6);

        var fitting = Families.SelectMany(f => f.Variants).Where(v => !card.TooBig(catalog.Find(ModelProviders.Local, v.Id)!)).Select(v => v.Id);

        fitting.ShouldBe(["musicgen-small", "musicgen-stereo-small", "magnet-small-10secs", "magnet-small-30secs", "audio-magnet-small", "audioldm2-music",
            "audioldm2", "audioldm2-large"], ignoreOrder: true);
        catalog.Find(ModelProviders.Local, "yue-v1-exl2")!.MinVideoMemoryGb.ShouldBe(8, "0.1.59: its stage 1 needs about 6.1 to 6.5 GB; red on a 6 GB card");
        // Same decoder, same class: a melody or stereo MusicGen never below its plain twin.
        foreach (var (plain, twins) in new[]
        {
            ("musicgen-small", new[] { "musicgen-stereo-small" }),
            ("musicgen-medium", new[] { "musicgen-stereo-medium", "musicgen-melody", "musicgen-stereo-melody" }),
            ("musicgen-large", new[] { "musicgen-stereo-large", "musicgen-melody-large", "musicgen-stereo-melody-large" }),
        })
        {
            twins.ShouldAllBe(t => catalog.Find(ModelProviders.Local, t)!.MinVideoMemoryGb == catalog.Find(ModelProviders.Local, plain)!.MinVideoMemoryGb);
        }

        // The official floors: DiffRhythm base 8 GB (its README), AudioCraft's 16 GB for the medium MAGNeT models, and the 6 GB table of
        // YuE-exllamav2 that shows the original 16-bit stage 2 (which YuE 0.5B uses) out of memory.
        DiffRhythmModels.Current.Concat(DiffRhythmModels.Legacy).Append(MagnetModels.Medium10).Append(YueModels.Stage1Small)
            .ShouldAllBe(v => card.TooBig(catalog.Find(ModelProviders.Local, v.Id)!));
        // The official floors: SongGeneration 10 to 22 GB (its low-memory mode already), YuE2 24 GB and 8 GB in its low-memory mode, YuE 7B 24 GB.
        SongGenerationModels.All.Concat(YueModels.Yue2Models).Append(YueModels.Stage1Large)
            .ShouldAllBe(v => card.TooBig(catalog.Find(ModelProviders.Local, v.Id)!));
        (card with { Nvidia = false }).TooBig(catalog.Find(ModelProviders.Local, "musicgen-large")!).ShouldBeFalse("without NVIDIA the NVIDIA note says it");
        (card with { Tier = VramTier.None }).TooBig(catalog.Find(ModelProviders.Local, "musicgen-large")!).ShouldBeFalse("an unknown card is not held against it");
        (card with { Tier = VramTier.Gb12 }).TooBig(catalog.Find(ModelProviders.Local, "musicgen-large")!).ShouldBeFalse();
    }

    [Fact]
    public void EveryFileIsPinnedToAHuggingFaceCommitInsideItsModelsFolders()
    {
        foreach (var variant in Families.SelectMany(f => f.Variants))
        {
            variant.Files.Select(f => f.ExecutablePath).ShouldBeUnique(variant.Id);
            foreach (var file in variant.Files)
            {
                file.Tool.ShouldBe(ToolKind.MusicWeights);
                file.Url.Scheme.ShouldBe(Uri.UriSchemeHttps);
                ToolManifest.AllowedHosts.ShouldContain(file.Url.Host);
                file.Url.Host.ShouldBe("huggingface.co");
                file.Url.AbsolutePath.ShouldMatch("^/[^/]+/[^/]+/resolve/[0-9a-f]{40}/[^.].*$");
                file.Sha256.ShouldMatch("^[0-9a-f]{64}$");
                file.ApproximateBytes.ShouldBeGreaterThan(0);
                file.ExecutablePath.ShouldNotContain("..");
                (file.ExecutablePath.StartsWith(variant.Folder + "/", StringComparison.Ordinal)
                    || (variant.SharedFolder.Length > 0 && file.ExecutablePath.StartsWith(variant.SharedFolder + "/", StringComparison.Ordinal))).ShouldBeTrue(file.ExecutablePath);
            }
        }

        MagnetModels.All.Where(v => v.Id.StartsWith("audio-", StringComparison.Ordinal)).ShouldAllBe(v => v.SharedFolder == "t5-large", "sound effects: t5-large");
        MagnetModels.All.Where(v => !v.Id.StartsWith("audio-", StringComparison.Ordinal)).ShouldAllBe(v => v.SharedFolder == "t5-base", "music: t5-base");
        MusicGenModels.Medium.Files.ShouldContain(f => f.ExecutablePath == "musicgen-medium/pytorch_model.bin", "Meta published no safetensors for it");
        AudioLdm2Models.All.ShouldAllBe(v => v.Files.Count == 26 && v.Files.Any(f => f.ExecutablePath == v.Folder + "/model_index.json"));
        SongGenerationModels.All.ShouldAllBe(v => v.Folder.StartsWith("songgeneration_", StringComparison.Ordinal) && v.SharedFolder == "runtime",
            "generate.py picks the version by the folder's name");
        YueModels.Yue2.Files.ShouldBe(YueModels.Yue2LowMemory.Files, "both YuE2 entries share one download");
        foreach (var family in Families.Where(f => f.Source is not null))
        {
            var source = family.Source!;
            source.Tool.ShouldBe(ToolKind.MusicCode);
            ToolManifest.AllowedHosts.ShouldContain(source.Url.Host);
            source.Url.Host.ShouldBe("codeload.github.com");
            var commit = source.Url.Segments[^1];
            commit.ShouldMatch("^[0-9a-f]{40}$");
            source.Root.ShouldEndWith("-" + commit);
            source.Sha256.ShouldMatch("^[0-9a-f]{64}$");
            source.ApproximateBytes.ShouldBeGreaterThan(1_000_000);
        }

        Families.Where(f => f.Source is not null).Select(f => LocalMusicComponentSource.SourceName(f, f.Source!)).ShouldBeUnique();
        LocalMusicComponentSource.SourceName(LocalMusicFamilies.Magnet, AudiocraftPins.Archive).ShouldBe("audiocraft");
    }

    [Fact]
    public void EachFamilyShipsAFullyPinnedCardOnlyLock()
    {
        foreach (var environment in Families.Select(f => f.Environment))
        {
            PythonFiles.HasLock(environment, environment.CudaBackend).ShouldBeTrue(environment.Key);
            PythonFiles.HasLock(environment, PythonPins.CpuBackend).ShouldBeFalse(environment.Key + ": far too slow on the processor to offer");
            var text = PythonFiles.Lock(environment, environment.CudaBackend);
            PythonFiles.ExcludeNewer(environment, environment.CudaBackend).ShouldBe("2026-11-01T00:00:00Z");
            var packages = text.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith('#')).ToList();
            packages.ShouldAllBe(p => p.Contains("==", StringComparison.Ordinal), "every package at an exact version");
            foreach (var asked in environment.Packages.Where(p => p.Contains("==", StringComparison.Ordinal)))
            {
                var (name, version) = (asked.Split("==")[0], asked.Split("==")[1]);
                packages.ShouldContain(p => p.StartsWith(name + "==" + version, StringComparison.Ordinal), environment.Key + ": " + asked);
            }
        }

        PythonFiles.Lock(LocalMusicEnvironments.MusicGen, "cu128").ShouldContain("torch==2.8.0+cu128");
        PythonFiles.Lock(LocalMusicEnvironments.AudioLdm2, "cu128").ShouldContain("transformers==4.47.1", Case.Sensitive, "AudioLDM 2's pipeline breaks on newer ones");
        var magnet = PythonFiles.Lock(LocalMusicEnvironments.Magnet, "cu121");
        magnet.ShouldContain("torch==2.1.0+cu121");
        magnet.ShouldContain("xformers==0.0.22.post7");
        magnet.Split('\n').Single(l => l.StartsWith("triton==", StringComparison.Ordinal))
            .ShouldContain("sys_platform == 'linux'", Case.Sensitive, "triton 2.1.0 has no Windows build");
        PythonFiles.Lock(LocalMusicEnvironments.DiffRhythm, "cu124").ShouldContain("transformers==4.49.0", Case.Sensitive, "newer ones break its transformer silently");
        PythonFiles.Lock(LocalMusicEnvironments.DiffRhythm, "cu124").ShouldContain("torch==2.6.0+cu124");
        var song = PythonFiles.Lock(LocalMusicEnvironments.SongGeneration, "cu124");
        song.ShouldContain("hydra-core==1.0.7");
        song.ShouldContain("omegaconf==2.2.0", Case.Sensitive, "its no-deps list at its own version, not the resolve's");
        song.Split('\n').Count(l => l.StartsWith("omegaconf==", StringComparison.Ordinal)).ShouldBe(1);
        PythonFiles.Lock(LocalMusicEnvironments.Yue2, "cu128").ShouldContain("torch==2.10.0+cu128", Case.Sensitive, "PyPI's 2.10.0 is processor-only on Windows");
        var yue = PythonFiles.Lock(LocalMusicEnvironments.Yue, "cu124");
        yue.ShouldContain("exllamav2 @ https://github.com/turboderp-org/exllamav2/releases/download/v0.2.8/exllamav2-0.2.8+cu124.torch2.6.0-cp311-cp311-win_amd64.whl ; sys_platform == 'win32'");
        yue.ShouldContain("flash_attn-2.7.4.post1+cu124torch2.6.0cxx11abiFALSE-cp311-cp311-win_amd64.whl ; sys_platform == 'win32'");
        PythonInstaller.CudaVersion("cu124").ShouldBe("12.4");
        PythonInstaller.CudaVersion("cu121").ShouldBe("12.1");
        PythonInstaller.CudaVersion("cu128").ShouldBe("12.8");
    }

    [Fact]
    public void ACardOnlyEnvironmentIsSupportedOnlyWithAUsableNvidiaCard()
    {
        var withCard = new PythonEnvironmentComponent(LocalMusicEnvironments.Magnet, Installer(new FakeGpu(6 * GiB)), _python);
        var without = new PythonEnvironmentComponent(LocalMusicEnvironments.Magnet, Installer(new FakeGpu(GpuReport.None)), _python);

        withCard.Supported.ShouldBeTrue();
        withCard.Code.ShouldBe("speech-env.magnet");
        withCard.Downloads().ShouldHaveSingleItem().Name.ShouldContain("CUDA 12.1");
        without.Supported.ShouldBeFalse("no processor build of the music models");
        Installer(new FakeGpu(GpuReport.None)).Backend(LocalMusicEnvironments.Magnet).ShouldBe(PythonPins.CpuBackend);
        Installer(new FakeGpu(6 * GiB)).Backend(LocalMusicEnvironments.Magnet).ShouldBe("cu121");
        new PythonEnvironmentComponent(PythonEnvironments.Xtts, Installer(new FakeGpu(6 * GiB)), _python).Downloads().ShouldHaveSingleItem().Name
            .ShouldContain("CUDA 12.8", Case.Sensitive, "the voices keep their text");
    }

    [Fact]
    public async Task ACardOnlyEnvironmentIsNeverInstalledForTheProcessor()
    {
        var runner = new FakeRunner();
        var installer = Installer(new FakeGpu(GpuReport.None), runner);
        InstallPython();

        var error = await Should.ThrowAsync<ToolInstallException>(() => installer.InstallEnvironmentAsync(LocalMusicEnvironments.MusicGen, null, CancellationToken.None));

        error.Code.ShouldBe("unsupported");
        runner.Runs.ShouldBeEmpty();
    }

    [Fact]
    public void TheComponentsAreEnvironmentSourceAndOneWeightsComponentPerModel()
    {
        var source = new LocalMusicComponentSource(new EmbeddedModelCatalog(), Installer(new FakeGpu(6 * GiB)), _python, Store(new FakeDownloader()), _layout);
        var byCode = source.Components.ToDictionary(c => c.Code);

        byCode.Keys.ShouldContain("speech-env.musicgen");
        byCode.Keys.ShouldContain("speech-env.audioldm2");
        byCode.Keys.ShouldContain("speech-env.magnet");
        byCode["music-code.audiocraft"].DependsOn.ShouldBeEmpty();
        byCode["music.magnet-small-10secs"].DependsOn.ShouldBe(["speech-env.magnet", "music-code.audiocraft"]);
        byCode["music.musicgen-small"].DependsOn.ShouldBe(["speech-env.musicgen"]);
        byCode["music.audioldm2-large"].Name.ShouldContain("CC-BY-NC-SA-4.0: NON-COMMERCIAL");
        byCode.Values.Where(c => c.Code.StartsWith(ComponentCodes.MusicModelPrefix, StringComparison.Ordinal)).Count().ShouldBe(34);
        source.Components.Select(c => c.Code).ShouldBeUnique("DiffRhythm 1.0 and 1.2 share one environment component");
        byCode["music.diffrhythm-1.0-base"].DependsOn.ShouldBe(["speech-env.diffrhythm", "music-code.diffrhythm-1.0"]);
        byCode["music.diffrhythm-1.2-full"].DependsOn.ShouldBe(["speech-env.diffrhythm", "music-code.diffrhythm"]);
        byCode["music-code.songgeneration"].Name.ShouldContain("non-commercial");
        byCode["music-code.yue2"].Name.ShouldContain("Apache-2.0");
        byCode["music.yue-v1-exl2"].Name.ShouldNotContain("NON-COMMERCIAL", Case.Sensitive, "YuE 1 is Apache-2.0");
        byCode["music.songgeneration-v2-medium"].Name.ShouldContain("NON-COMMERCIAL");
        source.Components.ShouldAllBe(c => !c.Ready, "nothing is there until installed");
        byCode["music.musicgen-small"].Downloads().ShouldHaveSingleItem().ApproximateBytes.ShouldBe(MusicGenModels.Small.Files.Sum(f => f.ApproximateBytes));
        foreach (var model in new EmbeddedModelCatalog().All.Where(m => m.Provider == ModelProviders.Local && Families.Any(f => f.Variants.Any(v => v.Id == m.Id))))
        {
            model.Requires.ShouldAllBe(code => byCode.ContainsKey(code), model.Id + ": every component it needs exists");
        }
    }

    [Fact]
    public async Task AModelsFilesComeOneByOneAndASharedFileOnlyOnce()
    {
        var downloader = new FakeDownloader();
        var store = Store(downloader);
        var family = TestFamily();

        await store.InstallModelAsync(family, family.Variants[0], null, CancellationToken.None);
        downloader.Asked.Select(a => a.ExecutablePath).ShouldBe(["one/model.bin", "encoder/model.safetensors"]);
        store.ModelReady(family, family.Variants[0]).ShouldBeTrue();
        store.ModelReady(family, family.Variants[1]).ShouldBeFalse();

        downloader.Asked.Clear();
        await store.InstallModelAsync(family, family.Variants[1], null, CancellationToken.None);
        downloader.Asked.Select(a => a.ExecutablePath).ShouldBe(["two/model.bin"], "the shared text encoder is there already");
        store.ModelReady(family, family.Variants[1]).ShouldBeTrue();

        // A new pin (another file list) counts as missing until installed again.
        var repinned = family.Variants[1] with { Files = [.. family.Variants[1].Files.Take(1), .. Files("encoder", "model.safetensors", 'c', 7)] };
        store.ModelReady(family, repinned).ShouldBeFalse();
        File.ReadAllText(_layout.ModelMarker(family, family.Variants[1])).ShouldBe(LocalMusicStore.Fingerprint(family.Variants[1]));
    }

    [Fact]
    public async Task AModelsSharedFilesAreInUseWhileItsFolderIsThereAndNeverALeftover()
    {
        var store = Store(new FakeDownloader());
        var family = TestFamily();
        var one = new LocalMusicWeightsComponent(family, family.Variants[0], [], string.Empty, false, store, _layout);
        var two = new LocalMusicWeightsComponent(family, family.Variants[1], [], string.Empty, false, store, _layout);
        var encoder = Path.Combine(_layout.FamilyFolder(family), "encoder");
        one.SharedFolders().ShouldBeEmpty("nothing is in use before its install starts");

        await store.InstallModelAsync(family, family.Variants[0], null, CancellationToken.None);
        Directory.CreateDirectory(_layout.Home);
        await File.WriteAllTextAsync(Path.Combine(_layout.Home, "cache.bin"), "x", TestContext.Current.CancellationToken);
        one.SharedFolders().ShouldBe([encoder, _layout.Home]);
        one.Folders().ShouldBe([_layout.ModelFolder(family, family.Variants[0])], "its Remove leaves the shared files to the other models");
        Leftovers().ShouldNotContain(encoder, "the clean-up never offers files a model still uses");
        Leftovers().ShouldNotContain(_layout.Home);
        Leftovers().ShouldNotContain(_layout.FamilyFolder(family));

        // The family's last model removed: its shared files are a leftover now.
        Directory.Delete(_layout.ModelFolder(family, family.Variants[0]), recursive: true);
        Leftovers().ShouldContain(encoder);

        IReadOnlyList<string> Leftovers() => global::TMConcept.Desktop.DiskSpace.LeftoverDiskSource.Unowned(_paths.ToolsDirectory,
            new[] { one, two }.SelectMany(c => c.Folders().Concat(c.SharedFolders())).Select(Path.GetFullPath).ToList()).ToList();
    }

    [Fact]
    public async Task TheSourceIsUnpackedCheckedAndReplacedWhole()
    {
        var family = LocalMusicFamilies.Magnet;
        var store = Store(new FakeDownloader());
        Directory.CreateDirectory(_layout.SourceFolder(family));
        await File.WriteAllTextAsync(Path.Combine(_layout.SourceFolder(family), "stale.py"), "old", TestContext.Current.CancellationToken);

        store.SourceReady(family).ShouldBeFalse();
        await store.InstallSourceAsync(family, null, CancellationToken.None);

        store.SourceReady(family).ShouldBeTrue();
        File.Exists(Path.Combine(_layout.SourceFolder(family), "audiocraft", "models", "magnet.py")).ShouldBeTrue();
        File.Exists(Path.Combine(_layout.SourceFolder(family), "stale.py")).ShouldBeFalse("a file the pinned commit does not have must not stay importable");
        Directory.Exists(_layout.Downloads(family)).ShouldBeFalse();
        store.SourceReady(LocalMusicFamilies.MusicGen).ShouldBeTrue("a family without a source needs none");

        var broken = Store(new FakeDownloader { WithoutCheckFile = true });
        var error = await Should.ThrowAsync<ToolInstallException>(() => broken.InstallSourceAsync(family, null, CancellationToken.None));
        error.Code.ShouldBe("archive");
    }

    [Fact]
    public async Task APieceIsOneRunOfTheFamilysScriptWithThePromptInAFile()
    {
        var runner = new FakeRunner { WritesWav = true };
        var (generator, model) = await ReadyAsync(runner);
        var output = Path.Combine(_paths.WorkDirectory, "music-generated");

        var piece = await generator.GenerateAsync(Request(model, output, "sakin bir piyano, sözsüz", 45), CancellationToken.None);

        var run = runner.Runs.ShouldHaveSingleItem();
        run.Program.ShouldBe(_python.EnvironmentPython("musicgen"));
        run.Arguments.Take(4).ShouldBe(["-X", "utf8", "-I", Path.Combine(_python.ScriptsFolder, "music_musicgen.py")]);
        File.Exists(Path.Combine(_python.ScriptsFolder, "music_musicgen.py")).ShouldBeTrue("the script is written before the run");
        Value(run, "--model").ShouldBe(_layout.ModelFolder(generator.Family, generator.Family.Variants[0]));
        Value(run, "--seconds").ShouldBe("10", "45 s asked, the model's longest is 10 s: a longer video loops it");
        Value(run, "--seed").ShouldBe("7");
        Value(run, "--extra").ShouldBe("yes", "the family's own arguments come last");
        (await File.ReadAllTextAsync(Value(run, "--prompt-file"), TestContext.Current.CancellationToken)).ShouldBe("sakin bir piyano, sözsüz");
        run.Arguments.ShouldNotContain("sakin bir piyano, sözsüz", "never on the command line");
        run.Environment["HF_HUB_OFFLINE"].ShouldBe("1");
        run.Environment["TRANSFORMERS_OFFLINE"].ShouldBe("1");
        run.Environment["HF_HOME"].ShouldStartWith(_layout.Root);
        piece.File.ShouldBe(Path.Combine(output, LocalMusicRunRules.PieceFile));
        piece.Usd.ShouldBe(0);
    }

    [Fact]
    public async Task TheScriptsFailureLineIsTheReasonAndACardOutOfMemoryIsAMemoryFailure()
    {
        var runner = new FakeRunner();
        var (generator, model) = await ReadyAsync(runner);
        runner.FailOutput = string.Join('\n', "TMC: loading the model", "Traceback (most recent call last):", "torch.OutOfMemoryError: CUDA out of memory.",
            "TMC-E-MEMORY: the graphics card ran out of memory (CUDA out of memory.)");

        var error = await Should.ThrowAsync<MusicGenerationException>(() => generator.GenerateAsync(Request(model, Path.Combine(_paths.WorkDirectory, "g"), "x", 10),
            CancellationToken.None));

        error.Code.ShouldBe(MusicFailure.Memory);
        error.Message.ShouldContain("TMC-E-MEMORY: the graphics card ran out of memory");
        error.Message.ShouldNotContain("Traceback", Case.Sensitive, "the traceback goes to the log only");

        runner.FailOutput = "ModuleNotFoundError: No module named 'xformers'";
        var crash = await Should.ThrowAsync<MusicGenerationException>(() => generator.GenerateAsync(Request(model, Path.Combine(_paths.WorkDirectory, "h"), "x", 10),
            CancellationToken.None));
        crash.Code.ShouldBe(MusicFailure.Process);
        crash.Message.ShouldContain("No module named 'xformers'", Case.Sensitive, "without its own line the general summary");
        LocalMusicOutput.Reason(null).ShouldBeEmpty();
    }

    [Fact]
    public async Task NothingRunsWhileAModelIsNotInstalled()
    {
        var runner = new FakeRunner();
        var generator = new MusicGenGenerator(Store(new FakeDownloader()), _layout, Installer(new FakeGpu(6 * GiB), runner), _python, runner,
            NullLogger<MusicGenGenerator>.Instance);
        var model = new EmbeddedModelCatalog().Find(ModelProviders.Local, "musicgen-small")!;

        generator.Handles(model).ShouldBeTrue();
        generator.Ready(model).ShouldBeFalse();
        var error = await Should.ThrowAsync<MusicGenerationException>(() => generator.GenerateAsync(Request(model, Path.Combine(_paths.WorkDirectory, "g"), "x", 10),
            CancellationToken.None));
        error.Code.ShouldBe(MusicFailure.Missing);
        runner.Runs.ShouldBeEmpty();
    }

    [Fact]
    public void EachFamilyHandlesOnlyItsOwnModelsAndAddsItsOwnArguments()
    {
        var runner = new FakeRunner();
        var installer = Installer(new FakeGpu(6 * GiB), runner);
        var store = Store(new FakeDownloader());
        var musicGen = new MusicGenGenerator(store, _layout, installer, _python, runner, NullLogger<MusicGenGenerator>.Instance);
        var magnet = new MagnetGenerator(store, _layout, installer, _python, runner, NullLogger<MagnetGenerator>.Instance);
        var audioLdm2 = new AudioLdm2Generator(store, _layout, installer, _python, runner, NullLogger<AudioLdm2Generator>.Instance);
        var catalog = new EmbeddedModelCatalog();

        musicGen.Handles(catalog.Find(ModelProviders.Local, "musicgen-stereo-large")!).ShouldBeTrue();
        musicGen.Handles(catalog.Find(ModelProviders.Local, "magnet-small-10secs")!).ShouldBeFalse();
        musicGen.Handles(catalog.Find(ModelProviders.Local, RakkaseMusicGenerator.ModelId)!).ShouldBeFalse();
        magnet.Handles(catalog.Find(ModelProviders.Local, "audio-magnet-medium")!).ShouldBeTrue();
        audioLdm2.Handles(catalog.Find(ModelProviders.Local, "audioldm2-large")!).ShouldBeTrue();
        audioLdm2.Handles(new CatalogModel { Provider = "fal", Id = "audioldm2" }).ShouldBeFalse("only the local model");

        musicGen.Arguments(MusicGenModels.Small, "s.py", "p.txt", 30, 1, "o.wav").TakeLast(2).ShouldBe(["--guidance", "3.0"]);
        audioLdm2.Arguments(AudioLdm2Models.Large, "s.py", "p.txt", 20, 1, "o.wav").TakeLast(4).ShouldBe(["--steps", "50", "--guidance", "3.5"]);
        var run = magnet.Arguments(MagnetModels.AudioSmall, "s.py", "p.txt", 10, 1, "o.wav").ToList();
        run[run.IndexOf("--code") + 1].ShouldBe(_layout.SourceFolder(LocalMusicFamilies.Magnet));
        run[run.IndexOf("--t5-name") + 1].ShouldBe("t5-large");
        run[run.IndexOf("--t5") + 1].ShouldBe(Path.Combine(_layout.FamilyFolder(LocalMusicFamilies.Magnet), "t5-large"));
        LocalMusicGeneratorBase.Seconds(MagnetModels.Small30, 120).ShouldBe(30);
        LocalMusicGeneratorBase.Seconds(MagnetModels.Small30, 12).ShouldBe(12);
    }

    [Fact]
    public void EveryLocalMusicModelMadeForACardIsAModelOfTheCardQueue()
    {
        var source = new MusicModelSource(new FakeGpu(6 * GiB));
        var catalog = new EmbeddedModelCatalog();

        source.Create(catalog.Find(ModelProviders.Local, "musicgen-small")!).ShouldNotBeNull().Device.ShouldBe(ModelDevice.Gpu);
        source.Create(catalog.Find(ModelProviders.Local, "audioldm2")!).ShouldNotBeNull();
        source.Create(catalog.Find(ModelProviders.Local, RakkaseMusicGenerator.ModelId)!).ShouldNotBeNull();
        source.Create(catalog.Find(ModelProviders.Local, ScoreRules.ModelId)!).ShouldBeNull("Program composes runs on the processor");
        new MusicModelSource(new FakeGpu(GpuReport.None)).Create(catalog.Find(ModelProviders.Local, "musicgen-small")!).ShouldNotBeNull().Device.ShouldBe(ModelDevice.Cpu);
    }

    [Fact]
    public void DiffRhythmsTwoCodeVersionsShareTheirFolderButNotTheirSource()
    {
        var current = LocalMusicFamilies.DiffRhythm;
        var legacy = LocalMusicFamilies.DiffRhythmLegacy;

        _layout.FamilyFolder(legacy).ShouldBe(_layout.FamilyFolder(current));
        _layout.SharedFolder(legacy, DiffRhythmModels.Base10).ShouldBe(_layout.SharedFolder(current, DiffRhythmModels.Base12), "the VAE and MuQ-MuLan once");
        _layout.SourceFolder(legacy).ShouldNotBe(_layout.SourceFolder(current));
        _layout.Downloads(legacy).ShouldNotBe(_layout.Downloads(current));
        DiffRhythmGeneratorBase.Frames(DiffRhythmModels.Base12).ShouldBe(2048);
        DiffRhythmGeneratorBase.Frames(DiffRhythmModels.Full10).ShouldBe(6144);
    }

    [Fact]
    public void EachNewFamilyAddsItsOwnArguments()
    {
        var runner = new FakeRunner();
        var installer = Installer(new FakeGpu(6 * GiB), runner);
        var store = Store(new FakeDownloader());
        var diffRhythm = new DiffRhythmGenerator(store, _layout, installer, _python, runner, NullLogger<DiffRhythmGenerator>.Instance);
        var legacy = new DiffRhythmLegacyGenerator(store, _layout, installer, _python, runner, NullLogger<DiffRhythmLegacyGenerator>.Instance);
        var song = new SongGenerationGenerator(store, _layout, installer, _python, runner, NullLogger<SongGenerationGenerator>.Instance);
        var yue2 = new Yue2Generator(store, _layout, installer, _python, runner, NullLogger<Yue2Generator>.Instance);
        var yue = new YueGenerator(store, _layout, installer, _python, runner, NullLogger<YueGenerator>.Instance);
        var catalog = new EmbeddedModelCatalog();

        diffRhythm.Handles(catalog.Find(ModelProviders.Local, "diffrhythm-1.2-full")!).ShouldBeTrue();
        diffRhythm.Handles(catalog.Find(ModelProviders.Local, "diffrhythm-1.0-full")!).ShouldBeFalse("the 1.0 models run with the 1.0 code");
        legacy.Handles(catalog.Find(ModelProviders.Local, "diffrhythm-1.0-full")!).ShouldBeTrue();

        var run = diffRhythm.Arguments(DiffRhythmModels.Full12, "s.py", "p.txt", 120, 1, "o.wav", "l.txt").ToList();
        run.ShouldNotContain("--lyrics-file", "DiffRhythm makes instrumental pieces only");
        run[run.IndexOf("--frames") + 1].ShouldBe("6144");
        run[run.IndexOf("--code-version") + 1].ShouldBe("1.2");
        run[run.IndexOf("--shared") + 1].ShouldBe(Path.Combine(_layout.FamilyFolder(LocalMusicFamilies.DiffRhythm), "shared"));
        var old = legacy.Arguments(DiffRhythmModels.Base10, "s.py", "p.txt", 60, 1, "o.wav").ToList();
        old[old.IndexOf("--code-version") + 1].ShouldBe("1.0");
        old[old.IndexOf("--code") + 1].ShouldBe(_layout.SourceFolder(LocalMusicFamilies.DiffRhythmLegacy));

        var v2 = song.Arguments(SongGenerationModels.V2Medium, "s.py", "p.txt", 120, 1, "o.wav", "l.txt").ToList();
        v2[v2.IndexOf("--lyrics-file") + 1].ShouldBe("l.txt");
        v2[v2.IndexOf("--version") + 1].ShouldBe("v2");
        v2[v2.IndexOf("--model") + 1].ShouldEndWith("songgeneration_v2_medium");
        v2[v2.IndexOf("--runtime") + 1].ShouldBe(Path.Combine(_layout.FamilyFolder(LocalMusicFamilies.SongGeneration), "runtime"));
        var v1 = song.Arguments(SongGenerationModels.BaseFull, "s.py", "p.txt", 120, 1, "o.wav", "l.txt").ToList();
        v1[v1.IndexOf("--version") + 1].ShouldBe("v1");

        var low = yue2.Arguments(YueModels.Yue2LowMemory, "s.py", "p.txt", 120, 1, "o.wav", "l.txt").ToList();
        low.ShouldContain("--low-vram");
        low[low.IndexOf("--budget") + 1].ShouldBe("8");
        var full = yue2.Arguments(YueModels.Yue2, "s.py", "p.txt", 120, 1, "o.wav", "l.txt").ToList();
        full.ShouldNotContain("--low-vram");
        full[full.IndexOf("--budget") + 1].ShouldBe("24");
        full[full.IndexOf("--weights") + 1].ShouldBe(Path.Combine(_layout.FamilyFolder(LocalMusicFamilies.Yue2), "yue2-3b"));

        var quantized = yue.Arguments(YueModels.Quantized, "s.py", "p.txt", 60, 1, "o.wav", "l.txt").ToList();
        quantized.ShouldContain("--exl2");
        quantized[quantized.IndexOf("--stage1") + 1].ShouldBe(Path.Combine(_layout.ModelFolder(LocalMusicFamilies.Yue, YueModels.Quantized), "s1"));
        quantized[quantized.IndexOf("--stage2") + 1].ShouldBe(Path.Combine(_layout.ModelFolder(LocalMusicFamilies.Yue, YueModels.Quantized), "s2"));
        var small = yue.Arguments(YueModels.Stage1Small, "s.py", "p.txt", 60, 1, "o.wav", "l.txt").ToList();
        small.ShouldNotContain("--exl2");
        small[small.IndexOf("--stage1") + 1].ShouldBe(_layout.ModelFolder(LocalMusicFamilies.Yue, YueModels.Stage1Small));
        small[small.IndexOf("--stage2") + 1].ShouldBe(Path.Combine(_layout.FamilyFolder(LocalMusicFamilies.Yue), "shared", "s2-1b"));
        small[small.IndexOf("--xcodec") + 1].ShouldBe(Path.Combine(_layout.FamilyFolder(LocalMusicFamilies.Yue), "shared", "xcodec"));
        LocalMusicGeneratorBase.Seconds(YueModels.Stage1Small, 120).ShouldBe(60);
    }

    [Theory]
    [InlineData(false, "[verse]\nla la", "[verse]\nla la")]
    [InlineData(true, "[verse]\nla la", "")]
    public async Task ASingingFamilyGetsTheWordsInAFileAndAnInstrumentalPieceAnEmptyOne(bool instrumental, string words, string written)
    {
        var runner = new FakeRunner { WritesWav = true };
        var (generator, model) = await ReadyAsync(runner, lyrics: true);
        var output = Path.Combine(_paths.WorkDirectory, "song");
        var request = Request(model, output, "pop", 30) with { Instrumental = instrumental, Lyrics = words };

        await generator.GenerateAsync(request, CancellationToken.None);

        var run = runner.Runs.ShouldHaveSingleItem();
        var file = Value(run, "--lyrics-file");
        file.ShouldBe(Path.Combine(output, LocalMusicRunRules.LyricsFile));
        (await File.ReadAllTextAsync(file, TestContext.Current.CancellationToken)).ShouldBe(written);
        run.Arguments.ShouldNotContain(words, "never on the command line");
    }

    [Fact]
    public async Task AFamilyThatDoesNotSingGetsNoWords()
    {
        var runner = new FakeRunner { WritesWav = true };
        var (generator, model) = await ReadyAsync(runner);

        await generator.GenerateAsync(Request(model, Path.Combine(_paths.WorkDirectory, "plain"), "pop", 10) with { Instrumental = false, Lyrics = "la" },
            CancellationToken.None);

        runner.Runs.ShouldHaveSingleItem().Arguments.ShouldNotContain("--lyrics-file");
        File.Exists(Path.Combine(_paths.WorkDirectory, "plain", LocalMusicRunRules.LyricsFile)).ShouldBeFalse();
    }

    private static string Licence(LocalMusicFamily family) =>
        family == LocalMusicFamilies.AudioLdm2 ? "cc-by-nc-sa-4.0"
        : family == LocalMusicFamilies.DiffRhythm || family == LocalMusicFamilies.DiffRhythmLegacy ? "diffrhythm"
        : family == LocalMusicFamilies.SongGeneration ? "songgeneration"
        : family == LocalMusicFamilies.Yue2 ? "yue2"
        : family == LocalMusicFamilies.Yue ? "apache-2.0"
        : "cc-by-nc-4.0";

    private async Task<(TestGenerator Generator, CatalogModel Model)> ReadyAsync(FakeRunner runner, bool lyrics = false)
    {
        var installer = Installer(new FakeGpu(6 * GiB), runner);
        var store = Store(new FakeDownloader());
        var family = TestFamily() with { Environment = LocalMusicEnvironments.MusicGen, Script = "music_musicgen.py", Lyrics = lyrics };
        var environment = _python.EnvironmentPython(family.Environment.Key);
        Directory.CreateDirectory(Path.GetDirectoryName(environment)!);
        await File.WriteAllTextAsync(environment, "fake", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(_python.EnvironmentMarker(family.Environment.Key), PythonInstaller.Fingerprint(family.Environment, "cu128"),
            TestContext.Current.CancellationToken);
        await store.InstallModelAsync(family, family.Variants[0], null, CancellationToken.None);
        var generator = new TestGenerator(family, store, _layout, installer, _python, runner);
        var model = new CatalogModel { Provider = ModelProviders.Local, Id = family.Variants[0].Id, Name = "Test", Capabilities = ModelCapability.Music };
        generator.Ready(model).ShouldBeTrue();
        return (generator, model);
    }

    private static string Value(ProcessStart run, string name) => run.Arguments[run.Arguments.ToList().IndexOf(name) + 1];

    private static MusicGenerationRequest Request(CatalogModel model, string output, string prompt, int seconds) =>
        new(new ResolvedModel(model, new ApiKey { Provider = ModelProviders.Local }, string.Empty), prompt, seconds, null, true, output, 7);

    /// <summary>A family of two small models that share one text encoder (sizes of a few bytes: nothing large is ever written).</summary>
    private static LocalMusicFamily TestFamily() => new("test", "Test", LocalMusicEnvironments.MusicGen, "music_musicgen.py",
    [
        new LocalMusicVariant("test-one", "one", 10, [.. Files("one", "model.bin", 'a', 5), .. Files("encoder", "model.safetensors", 'b', 3)], "encoder"),
        new LocalMusicVariant("test-two", "two", 10, [.. Files("two", "model.bin", 'd', 4), .. Files("encoder", "model.safetensors", 'b', 3)], "encoder"),
    ]);

    private static IReadOnlyList<ToolArchive> Files(string folder, string path, char hash, long bytes) =>
        LocalMusicFiles.From("test/" + folder, new string('0', 40), folder, [new PinnedFile(path, new string(hash, 64), bytes)]);

    private LocalMusicStore Store(FakeDownloader downloader) => new(_layout, downloader, NullLogger<LocalMusicStore>.Instance);

    private PythonInstaller Installer(IGpuInfo gpu, FakeRunner? runner = null) =>
        new(_python, new UvTools(Path.Combine(_paths.ToolsDirectory, "uv.exe")), runner ?? new FakeRunner(), gpu, NullLogger<PythonInstaller>.Instance);

    /// <summary>The app's Python, as its marker names it.</summary>
    private void InstallPython()
    {
        var interpreter = Path.Combine(_python.PythonFolder, "cpython-3.11.18", "python.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(interpreter)!);
        File.WriteAllText(interpreter, "fake");
        File.WriteAllText(_python.PythonMarker, interpreter);
    }

    /// <summary>A generator of the test family whose own argument is "--extra yes".</summary>
    private sealed class TestGenerator(LocalMusicFamily family, LocalMusicStore store, LocalMusicLayout layout, PythonInstaller python, PythonLayout pythonLayout,
        IProcessRunner runner) : LocalMusicGeneratorBase(family, store, layout, python, pythonLayout, runner, NullLogger.Instance)
    {
        protected override IReadOnlyList<string> FamilyArguments(LocalMusicVariant variant) => ["--extra", "yes"];
    }

    /// <summary>Writes a source archive with its top folder and check file, or a weight file of its pinned size (no network).</summary>
    private sealed class FakeDownloader : IArchiveDownloader
    {
        public List<ToolArchive> Asked { get; } = [];

        public bool WithoutCheckFile { get; init; }

        public Task DownloadAsync(ToolArchive archive, string file, IProgress<ToolProgress>? progress, CancellationToken cancellationToken)
        {
            Asked.Add(archive);
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            if (archive.Tool == ToolKind.MusicCode)
            {
                using var zip = ZipFile.Open(file, ZipArchiveMode.Create);
                using var writer = new StreamWriter(zip.CreateEntry(archive.Root + "/" + (WithoutCheckFile ? "README.md" : archive.ExecutablePath)).Open());
                writer.Write("# fake");
            }
            else
            {
                using var stream = File.Create(file);
                stream.SetLength(archive.ApproximateBytes);
            }

            return Task.CompletedTask;
        }
    }

    /// <summary>A script run writes the WAV it is given (or fails with <see cref="FailOutput"/>); nothing else runs.</summary>
    private sealed class FakeRunner : IProcessRunner
    {
        public List<ProcessStart> Runs { get; } = [];

        public string? FailOutput { get; set; }

        public bool WritesWav { get; init; }

        public Task<ProcessResult> RunAsync(ProcessStart start, Action<string>? onLine, CancellationToken cancellationToken)
        {
            Runs.Add(start);
            if (FailOutput is not null)
            {
                return Task.FromResult(new ProcessResult(2, FailOutput));
            }

            if (WritesWav)
            {
                var output = start.Arguments[start.Arguments.ToList().IndexOf("--out") + 1];
                File.WriteAllBytes(output, [1, 2, 3]);
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
