using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using TMConcept.Core.AppOllama;
using TMConcept.Core.Components;
using TMConcept.Core.Endpoints;
using TMConcept.Core.Hardware;
using TMConcept.Core.Models;
using TMConcept.Core.Tools;

namespace TMConcept.Tests.Core;

/// <summary>
/// The models of the app's own Ollama copy (0.1.20): read from its models folder, recommended per card class with exact tags, a local model
/// that the graphics card coordinator unloads with <c>keep_alive: 0</c>, and a component that pulls only when it is installed (after the
/// user's confirmation). Fakes only: no server, no network.
/// </summary>
public sealed class AppOllamaModelTests : IDisposable
{
    private const long GiB = 1024L * 1024 * 1024;
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "tmconcept-tests", "app-models-" + Guid.NewGuid().ToString("N"));

    public AppOllamaModelTests()
    {
        Store = new AppModelStore(new AppOllamaLayout(Path.Combine(_folder, "ollama-models"), Path.Combine(_folder, "ollama-home")));
    }

    private AppModelStore Store { get; }

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    [Fact]
    public void TheStoreReadsOllamasLayoutAndKeepsSharedBlobsOnRemoval()
    {
        var gemma = AppModelFiles.Write(Store, "gemma4:e4b-it-qat", "weights e4b", "shared license");
        AppModelFiles.Write(Store, "gemma4:e2b-it-qat", "weights e2b", "shared license");

        Store.IsInstalled("gemma4:e4b-it-qat").ShouldBeTrue();
        Store.List().Select(m => m.Tag).ShouldBe(["gemma4:e2b-it-qat", "gemma4:e4b-it-qat"]);
        Store.ManifestPath("gemma4:e4b-it-qat").ShouldEndWith(Path.Combine("manifests", "registry.ollama.ai", "library", "gemma4", "e4b-it-qat"));

        var owned = Store.OwnedPaths("gemma4:e4b-it-qat");
        owned.ShouldContain(Store.ManifestPath("gemma4:e4b-it-qat"));
        owned.ShouldContain(Store.BlobPath(gemma[1])!, "its own weights go");
        owned.ShouldNotContain(Store.BlobPath(gemma[2])!, "a layer the other model uses stays");

        // A blob that is gone makes the model incomplete (not offered in the pickers).
        File.Delete(Store.BlobPath(gemma[1])!);
        Store.IsInstalled("gemma4:e4b-it-qat").ShouldBeFalse();
        Store.HasManifest("gemma4:e4b-it-qat").ShouldBeTrue();
    }

    [Theory]
    [InlineData("../../etc/passwd")]
    [InlineData("qwen3.5:../../x")]
    [InlineData("C:\\Windows:x")]
    [InlineData("")]
    public void ATagThatIsNotAnOllamaTagNeverBecomesAPath(string tag)
    {
        AppModelStore.IsValidTag(tag).ShouldBeFalse();
        Should.Throw<ArgumentException>(() => Store.ManifestPath(tag));
        Store.HasManifest(tag).ShouldBeFalse();
    }

    [Fact]
    public void EveryRecommendationIsAnExactTagOfTheCatalogue()
    {
        AppModelCatalog.Models.Select(m => m.Tag).ShouldBeUnique();
        foreach (var model in AppModelCatalog.Models)
        {
            AppModelStore.IsValidTag(model.Tag).ShouldBeTrue(model.Tag);
            model.Tag.ShouldNotEndWith(":latest");
            model.Tag.ShouldContain(':');
            model.Family.ShouldBeOneOf("qwen3.5", "gemma4", "gpt-oss");
            model.Bytes.ShouldBeGreaterThan(1_000_000_000);
            model.ContextTokens.ShouldBeLessThanOrEqualTo(model.TrainedContextTokens);
            model.SourceUrl.ShouldStartWith("https://ollama.com/library/" + model.Family);
            model.LicenseUse.ShouldBe(LicenseUse.Commercial);
        }

        foreach (var tier in VramTiers.All)
        {
            AppModelAdvisor.For(tier).Count.ShouldBeGreaterThanOrEqualTo(2, tier.ToString());
        }

        AppModelCatalog.Recommended.Values.SelectMany(t => t).All(t => AppModelAdvisor.Find(t) is not null).ShouldBeTrue();
    }

    [Fact]
    public void TheCardClassDecidesWhatFits()
    {
        // The developer's 6 GB card is offered a Gemma 4 model of the 4B class; its colour says honestly whether it fits.
        var six = AppModelAdvisor.For(VramTier.Gb6);
        six.ShouldContain(m => m.Family == "gemma4" && m.Tag.StartsWith("gemma4:e4b", StringComparison.Ordinal));
        six.Count(m => AppModelAdvisor.Fit(m, 6 * GiB) == AppModelFit.Fits).ShouldBeGreaterThanOrEqualTo(2);

        var qwen4 = AppModelAdvisor.Find("qwen3.5:4b-q4_K_M")!;
        AppModelAdvisor.Fit(qwen4, 4 * GiB).ShouldBe(AppModelFit.Fits);
        AppModelAdvisor.Fit(AppModelAdvisor.Find("gpt-oss:20b")!, 8 * GiB).ShouldBe(AppModelFit.TooLarge);
        AppModelAdvisor.Fit(AppModelAdvisor.Find("gpt-oss:20b")!, 16 * GiB).ShouldBe(AppModelFit.Fits, "a friend's 16 GB card runs gpt-oss 20B");
        AppModelAdvisor.Fit(qwen4, null).ShouldBe(AppModelFit.Processor);
        AppModelAdvisor.CardBytes(qwen4).ShouldBe(qwen4.Bytes + (qwen4.KvBytesPerToken * qwen4.ContextTokens) + AppOllamaDefaults.OverheadBytes);
    }

    [Fact]
    public void EveryRecommendedModelFitsACardOfItsClass()
    {
        foreach (var (tier, tags) in AppModelCatalog.Recommended)
        {
            if (tier == VramTier.None)
            {
                continue;
            }

            AppModelAdvisor.Fit(AppModelAdvisor.Find(tags[0])!, (int)tier * GiB).ShouldBe(AppModelFit.Fits, $"the first model of the {(int)tier} GB class");
            foreach (var tag in tags)
            {
                var fit = AppModelAdvisor.Fit(AppModelAdvisor.Find(tag)!, (int)tier * GiB);
                if (tier == VramTier.Gb6 && tag == "gemma4:e4b-it-qat")
                {
                    // The developer's expectation for a 6 GB card: shown red (part in main memory), after the models that fit.
                    fit.ShouldBe(AppModelFit.TooLarge);
                    tags[^1].ShouldBe(tag);
                    continue;
                }

                fit.ShouldBe(AppModelFit.Fits, $"{tag} in the {(int)tier} GB class");
            }
        }

        AppModelCatalog.Recommended[VramTier.Gb24].ShouldContain("gemma4:26b-a4b-it-qat", "it needs about 17 GiB with its context");
    }

    [Fact]
    public void AnInstalledModelTheCatalogueNoLongerNamesIsStillAComponent()
    {
        // A tag an older version offered: it stays in the pickers, so the storage manager must list and remove it too.
        AppModelFiles.Write(Store, "qwen3:8b", "old weights", "shared");
        AppModelFiles.Write(Store, "qwen3.5:4b-q4_K_M", "weights", "shared");
        var registry = new AppModelRegistry(Store, NullLogger<AppModelRegistry>.Instance);
        var source = new AppModelComponentSource(Store, registry, new FakeAppOllamaRuntime(), new Providers.FakeRunningAppServer("http://127.0.0.1:11534"), new FakeAppOllamaApi(),
            NullLoggerFactory.Instance);
        var planner = new ComponentPlanner([source], NullLogger<ComponentPlanner>.Instance);

        var old = planner.Find(ComponentCodes.AppOllamaModelPrefix + "qwen3:8b").ShouldNotBeNull();
        planner.All.Count.ShouldBe(AppModelCatalog.Models.Count + 1);
        old.Ready.ShouldBeTrue();
        old.Folders().ShouldContain(Store.ManifestPath("qwen3:8b"));
        old.Folders().Count.ShouldBe(3, "its manifest, its config and its own weights; the shared layer stays");
        planner.Find(ComponentCodes.AppOllamaModelPrefix + "qwen3:8b").ShouldBeSameAs(old, "one component per tag");

        File.Delete(Store.ManifestPath("qwen3:8b"));
        registry.Refresh();
        planner.Find(ComponentCodes.AppOllamaModelPrefix + "qwen3:8b").ShouldBeNull();
    }

    [Fact]
    public void FilesNoModelUsesAreFoundButNeverABlobAModelNeeds()
    {
        var qwen = AppModelFiles.Write(Store, "qwen3.5:4b-q4_K_M", "weights");
        var blobs = Path.GetDirectoryName(Store.BlobPath(qwen[0])!)!;
        var partial = Path.Combine(blobs, "sha256-" + new string('a', 64) + "-partial-0");
        File.WriteAllText(partial, "half a layer");
        var orphan = Path.Combine(blobs, "sha256-" + new string('b', 64));
        File.WriteAllText(orphan, "a layer of a removed model");

        Store.UnreferencedBlobs().ShouldBe(new[] { partial, orphan }.Order(StringComparer.Ordinal).ToList());

        // A manifest that cannot be read might name anything: nothing is offered for removal then.
        var broken = Store.ManifestPath("gemma4:e2b-it-qat");
        Directory.CreateDirectory(Path.GetDirectoryName(broken)!);
        File.WriteAllText(broken, "{ not json");
        Store.UnreferencedBlobs().ShouldBeEmpty();
    }

    [Fact]
    public void InstalledModelsBecomeFreeTextModelsOfEveryPicker()
    {
        var registry = new AppModelRegistry(Store, NullLogger<AppModelRegistry>.Instance);
        registry.Catalog.ShouldBeEmpty();
        var changes = 0;
        registry.Changed += (_, _) => changes++;

        AppModelFiles.Write(Store, "qwen3.5:9b-q4_K_M", "weights");
        registry.Refresh();

        changes.ShouldBe(1);
        var entry = registry.Catalog.ShouldHaveSingleItem();
        entry.Provider.ShouldBe(ModelProviders.AppOllama);
        entry.Id.ShouldBe("qwen3.5:9b-q4_K_M");
        entry.Capabilities.ShouldBe(ModelCapability.Chat | ModelCapability.Schema | ModelCapability.Vision);
        entry.ContextTokens.ShouldBe(16_384);
        entry.MaxOutputTokens.ShouldBe(EndpointCodes.DefaultMaxOutput(16_384));
        entry.Requires.ShouldBe([ComponentCodes.AppOllama, ComponentCodes.AppOllamaModelPrefix + "qwen3.5:9b-q4_K_M"]);
        entry.InputUsd.ShouldBe(0);
        StepModelResolver.NeedsKey(ModelProviders.AppOllama).ShouldBeFalse();

        registry.Refresh();
        changes.ShouldBe(1, "nothing changed on disk");
    }

    [Fact]
    public async Task TheCoordinatorUnloadsAnAppModelWithKeepAliveZeroBeforeTheNextModel()
    {
        var api = new FakeAppOllamaApi();
        using var server = new AppOllamaServer(new FakeAppOllamaRuntime(), new FakeAppOllamaLauncher(), api,
            new AppOllamaLayout(Path.Combine(_folder, "m"), Path.Combine(_folder, "h")), TimeProvider.System, NullLogger<AppOllamaServer>.Instance);
        AppModelFiles.Write(Store, "gemma4:e4b-it-qat", "weights");
        var registry = new AppModelRegistry(Store, NullLogger<AppModelRegistry>.Instance);
        var source = new AppOllamaModelSource(registry, server, api, new FakeCardInfo(8 * GiB), NullLogger<AppOllamaModelSource>.Instance);
        var gpu = new GpuCoordinator(new ModelDirectory([source]), new FakeVramProbe(new VramReading(8 * GiB, 7 * GiB, true, "test")), TestWaiters.Fast(),
            TimeProvider.System, NullLogger<GpuCoordinator>.Instance, [new AppOllamaResidentSweep(server, api, NullLogger<AppOllamaResidentSweep>.Instance)]);

        var model = registry.Find("gemma4:e4b-it-qat")!;
        await using (var lease = await gpu.AcquireAsync(model, CancellationToken.None))
        {
            lease.OnCard.ShouldBeTrue();
            server.Running.ShouldBeTrue("the lease starts the app's server");
            api.Loaded.Add(model.Id);
        }

        var image = new FakeLocalModel("z-image", videoGb: 6);
        await using (await gpu.AcquireAsync(image, null, CancellationToken.None))
        {
            api.Unloads.ShouldBe(["gemma4:e4b-it-qat"]);
            image.Loads.ShouldBe(1);
        }

        server.Running.ShouldBeTrue("a confirmed unload keeps the server");
    }

    [Fact]
    public async Task AnUnloadTheServerDoesNotConfirmEndsTheServer()
    {
        var api = new FakeAppOllamaApi { UnloadWorks = false };
        using var server = new AppOllamaServer(new FakeAppOllamaRuntime(), new FakeAppOllamaLauncher(), api,
            new AppOllamaLayout(Path.Combine(_folder, "m"), Path.Combine(_folder, "h")), TimeProvider.System, NullLogger<AppOllamaServer>.Instance);
        var info = AppModelCatalog.Models[0];
        var model = new AppOllamaLocalModel(AppModelRegistry.Entry(info), info, server, api, onCard: true, NullLogger.Instance);

        await model.LoadAsync(CancellationToken.None);
        await model.UnloadAsync(CancellationToken.None);

        api.Unloads.ShouldBe([info.Tag]);
        server.Running.ShouldBeFalse("the server is ended so the card is certainly free");
        model.IsLoaded.ShouldBeFalse();
        model.Needs.ShouldHaveSingleItem().VideoMemoryBytes.ShouldBe(AppModelAdvisor.CardBytes(info));
        model.Tier.ShouldBe(AppModelAdvisor.Tier(info));

        var cpu = new AppOllamaLocalModel(AppModelRegistry.Entry(info), info, server, api, onCard: false, NullLogger.Instance);
        cpu.Device.ShouldBe(ModelDevice.Cpu);
        cpu.PlanMemory(new MemorySnapshot(VramReading.None, null), null).Placement.ShouldBe(MemoryPlacement.Processor);
    }

    [Fact]
    public async Task AModelIsPulledOnlyWhenItsComponentIsInstalled()
    {
        var api = new FakeAppOllamaApi
        {
            PullLines =
            {
                new AppModelPullProgress("pulling manifest", string.Empty, 0, 0),
                new AppModelPullProgress("pulling a", "sha256:a", 3_000_000_000, 1_500_000_000),
                new AppModelPullProgress("success", string.Empty, 0, 0),
            },
        };
        var info = AppModelAdvisor.Find("qwen3.5:4b-q4_K_M")!;
        api.OnPull = tag => AppModelFiles.Write(Store, tag, "weights");
        var launcher = new FakeAppOllamaLauncher();
        using var server = new AppOllamaServer(new FakeAppOllamaRuntime(), launcher, api,
            new AppOllamaLayout(Path.Combine(_folder, "m"), Path.Combine(_folder, "h")), TimeProvider.System, NullLogger<AppOllamaServer>.Instance);
        var registry = new AppModelRegistry(Store, NullLogger<AppModelRegistry>.Instance);
        var component = new AppModelComponent(info, Store, registry, new FakeAppOllamaRuntime(), server, api, NullLogger<AppModelComponent>.Instance);
        var planner = new ComponentPlanner([new ListSource(component)], NullLogger<ComponentPlanner>.Instance);

        // Planning (what the confirmation lists) and refreshing never download or start anything.
        var plan = await planner.PlanAsync([component.Code], CancellationToken.None);
        plan.Missing.ShouldBe([component]);
        plan.Downloads.ShouldHaveSingleItem().ApproximateBytes.ShouldBe(info.Bytes);
        plan.Downloads[0].Host.ShouldBe(AppOllamaDefaults.RegistryHost);
        await planner.RefreshAllAsync(CancellationToken.None);
        api.Pulls.ShouldBeEmpty();
        launcher.Started.ShouldBeEmpty();

        var shares = new List<double>();
        await component.InstallAsync(new SyncProgress(p => shares.Add(p.Share)), CancellationToken.None);

        api.Pulls.ShouldBe([info.Tag]);
        shares.ShouldContain(s => s > 0.4 && s < 0.5);
        shares[^1].ShouldBe(1);
        component.Ready.ShouldBeTrue();
        component.Folders().ShouldContain(Store.ManifestPath(info.Tag));
        registry.Find(info.Tag).ShouldNotBeNull("the pickers offer it at once");
    }

    [Fact]
    public async Task APullThatLeavesNoCompleteModelFails()
    {
        var api = new FakeAppOllamaApi();
        var info = AppModelAdvisor.Find("qwen3.5:2b-q4_K_M")!;
        using var server = new AppOllamaServer(new FakeAppOllamaRuntime(), new FakeAppOllamaLauncher(), api,
            new AppOllamaLayout(Path.Combine(_folder, "m"), Path.Combine(_folder, "h")), TimeProvider.System, NullLogger<AppOllamaServer>.Instance);
        var component = new AppModelComponent(info, Store, new AppModelRegistry(Store, NullLogger<AppModelRegistry>.Instance), new FakeAppOllamaRuntime(), server,
            api, NullLogger<AppModelComponent>.Instance);

        var error = await Should.ThrowAsync<ToolInstallException>(() => component.InstallAsync(null, CancellationToken.None));
        error.Code.ShouldBe("pull");

        var notInstalled = new AppModelComponent(info, Store, new AppModelRegistry(Store, NullLogger<AppModelRegistry>.Instance), new FakeAppOllamaRuntime(ready: false),
            new AppOllamaServer(new FakeAppOllamaRuntime(ready: false), new FakeAppOllamaLauncher(), api, new AppOllamaLayout(_folder, _folder), TimeProvider.System,
                NullLogger<AppOllamaServer>.Instance), api, NullLogger<AppModelComponent>.Instance);
        (await Should.ThrowAsync<ToolInstallException>(() => notInstalled.InstallAsync(null, CancellationToken.None))).Code.ShouldBe("ollama");
    }

    [Fact]
    public void ThePullTrackerSumsTheLayers()
    {
        var tracker = new AppModelPullTracker(4_000_000_000);
        tracker.Add(new AppModelPullProgress("pulling a", "sha256:a", 3_000_000_000, 1_000_000_000)).Share.ShouldBe(0.25);
        tracker.Add(new AppModelPullProgress("pulling b", "sha256:b", 1_000_000_000, 1_000_000_000)).Text.ShouldBe("2.0 / 4.0 GB");
        tracker.Add(new AppModelPullProgress("verifying sha256 digest", string.Empty, 0, 0)).Share.ShouldBe(0.5);
        tracker.Add(new AppModelPullProgress("success", string.Empty, 0, 0)).Share.ShouldBe(1);
    }

    private sealed class ListSource(params IComponent[] components) : IComponentSource
    {
        public IReadOnlyList<IComponent> Components { get; } = components;
    }

    private sealed class SyncProgress(Action<ToolProgress> report) : IProgress<ToolProgress>
    {
        public void Report(ToolProgress value) => report(value);
    }

    private sealed class FakeCardInfo(long bytes) : IGpuInfo
    {
        public GpuReport Detect() => new([new GpuAdapter(GpuVendor.Nvidia, "Test card", bytes)], true, true);
    }
}
