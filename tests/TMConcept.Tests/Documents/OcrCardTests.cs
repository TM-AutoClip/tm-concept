using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using TMConcept.Core.Chat;
using TMConcept.Core.Documents;
using TMConcept.Core.Hardware;
using TMConcept.Core.Models;
using TMConcept.Core.Tools;
using TMConcept.Documents.Ocr;
using TMConcept.Tests.Storage;

namespace TMConcept.Tests.Documents;

/// <summary>
/// 0.1.27, the local OCR on the graphics card: it is on the card only on Windows x64 with a card, the setting Automatic, the DirectML runtime
/// installed and DirectML not failed; each page asks the card for a lease and reads on the processor without it; a card that cannot open or
/// run the networks is given up for the run (said once), and the page is read again on the processor with the same text. Fake networks,
/// a fake coordinator and fake hardware: nothing runs on a graphics card here.
/// </summary>
public sealed class OcrCardTests : IDisposable
{
    private static readonly OcrDictionary Latin = OcrDictionary.Latin();

    private readonly TempPaths _paths = new();

    public void Dispose() => _paths.Dispose();

    [Fact]
    public void TheCardIsUsedOnlyWhereEverythingAllowsIt()
    {
        var setting = new Setting();
        var (model, store) = Card(setting: setting);
        model.Device.ShouldBe(ModelDevice.Cpu, "the runtime is not installed yet");
        store.Downloads().ShouldNotBeEmpty();

        Install(store);
        model.Device.ShouldBe(ModelDevice.Gpu);

        setting.Value = OcrAcceleration.Processor;
        model.Device.ShouldBe(ModelDevice.Cpu, "Processor only");
        setting.Value = OcrAcceleration.Automatic;

        Card(rid: "linux-x64").Model.Device.ShouldBe(ModelDevice.Cpu, "Linux reads on the processor");
        Card(rid: "linux-x64").Store.Supported.ShouldBeFalse();
        Card(adapters: false).Store.Supported.ShouldBeFalse("no graphics card");

        model.MarkUnusable(new InvalidOperationException("DirectML failed"));
        model.Device.ShouldBe(ModelDevice.Cpu, "a failure keeps the card unused for the run");
        model.FailureReason.ShouldBe("DirectML failed");
        model.TakeFailureNotice().ShouldBeTrue();
        model.TakeFailureNotice().ShouldBeFalse("said once");
    }

    [Fact]
    public async Task AnUnloadClosesTheNetworksOnTheCard()
    {
        var (model, store) = Card();
        Install(store);
        var networks = model.Networks(() => new OcrNetworkPair(new Net("x"), new Net("y")));
        model.Networks(() => throw new InvalidOperationException("opened once")).ShouldBeSameAs(networks);

        await model.UnloadAsync(CancellationToken.None);

        networks.Disposed.ShouldBeTrue();
        model.Networks(() => new OcrNetworkPair(new Net("x"), new Net("y"))).ShouldNotBeSameAs(networks, "opened again after the unload");
    }

    [Fact]
    public async Task WithoutTheCardsLeaseThePageIsReadOnTheProcessor()
    {
        var (model, store) = Card();
        Install(store);
        var gpu = new Factory("card");
        var cpu = new Factory("processor");
        var coordinator = new Coordinator { Grant = false };
        using var engine = Engine(model, cpu, gpu, coordinator);

        var read = await engine.RecognizeAsync(Page(1), CancellationToken.None);

        read.Text.ShouldBe("processor");
        coordinator.Asked.ShouldBe(1);
        gpu.Opened.ShouldBe(0);
        cpu.Opened.ShouldBe(1);
    }

    [Fact]
    public async Task EachPageTakesAndGivesBackItsOwnLease()
    {
        var (model, store) = Card();
        Install(store);
        var gpu = new Factory("card");
        var cpu = new Factory("processor");
        var coordinator = new Coordinator();
        using var engine = Engine(model, cpu, gpu, coordinator);

        for (var page = 1; page <= 3; page++)
        {
            (await engine.RecognizeAsync(Page(page), CancellationToken.None)).Text.ShouldBe("card");
        }

        (coordinator.Asked, coordinator.Released).ShouldBe((3, 3));
        gpu.Opened.ShouldBe(1, "the networks stay on the card between pages");
        cpu.Opened.ShouldBe(0, "the processor's networks are not opened while the card reads");
    }

    [Fact]
    public async Task ACardThatCannotOpenTheNetworksIsGivenUpAndTheProcessorReads()
    {
        var (model, store) = Card();
        Install(store);
        var gpu = new Factory("card") { FailOpen = true };
        var cpu = new Factory("processor");
        var coordinator = new Coordinator();
        using var engine = Engine(model, cpu, gpu, coordinator);

        (await engine.RecognizeAsync(Page(1), CancellationToken.None)).Text.ShouldBe("processor");
        (await engine.RecognizeAsync(Page(2), CancellationToken.None)).Text.ShouldBe("processor");

        model.Unusable.ShouldBeTrue();
        model.TakeFailureNotice().ShouldBeTrue();
        coordinator.Asked.ShouldBe(1, "the next page goes straight to the processor");
        coordinator.Released.ShouldBe(1);
    }

    [Fact]
    public async Task ACardThatFailsOnAPageIsGivenUpAndThePageIsReadAgainOnTheProcessor()
    {
        var (model, store) = Card();
        Install(store);
        var gpu = new Factory("Ayasofya") { FailRunAfter = 2 };
        var cpu = new Factory("Ayasofya");
        var coordinator = new Coordinator();
        using var engine = Engine(model, cpu, gpu, coordinator);

        var pages = new List<PageRecognition>();
        for (var page = 1; page <= 4; page++)
        {
            pages.Add(await engine.RecognizeAsync(Page(page), CancellationToken.None));
        }

        pages.ShouldAllBe(p => !p.Failed && p.Text == "Ayasofya", "the text is the same on either device");
        (coordinator.Asked, coordinator.Released).ShouldBe((3, 3), "pages 1-3 asked the card; page 3 failed there, its lease was given back");
        gpu.Pairs.Single().Disposed.ShouldBeTrue("the card's networks were closed");
        model.Unusable.ShouldBeTrue();
        engine.Recognized.ShouldBe(4);
    }

    [Fact]
    public void TheCardsRecogniserSeesWidthsInStepsAndTheSameValues()
    {
        var input = new OcrTensor([1, 2, 3, 4, 5, 6], [1, 1, 2, 3]);
        var padded = DirectMlOcrNetwork.Pad(input, 4);
        padded.Shape.ShouldBe([1, 1, 2, 4]);
        padded.Data.ShouldBe([1, 2, 3, 0, 4, 5, 6, 0]);
        DirectMlOcrNetwork.Pad(padded, 4).ShouldBeSameAs(padded);
    }

    private PaddleOcrRecognizer Engine(OcrGpuModel model, Factory cpu, Factory gpu, Coordinator coordinator)
    {
        var source = new HybridOcrNetworkSource(_paths.ToolsDirectory, cpu, gpu, model, coordinator, NullLogger.Instance);
        return new PaddleOcrRecognizer(new LocalOcrTests.FixedDecoder(LocalOcrTests.Page(400, 200, (20, 40, 200, 30))), source, Latin, NullLogger.Instance);
    }

    private static PageImage Page(int number) => new(number, [new ImagePart([1, 2, 3], "image/png")]);

    private (OcrGpuModel Model, DirectMlRuntimeComponent Store) Card(string rid = DirectMlRuntimeStore.Rid, bool adapters = true, Setting? setting = null)
    {
        var report = adapters ? new GpuReport([new GpuAdapter(GpuVendor.Amd, "Radeon", 8L << 30)], false, true) : GpuReport.None;
        var store = new DirectMlRuntimeStore(_paths, new Platform(rid), new Desktop.FakeGpu(report), new RefusingDownloader(), NullLogger<DirectMlRuntimeStore>.Instance);
        return (new OcrGpuModel(store, setting ?? new Setting(), NullLogger<OcrGpuModel>.Instance), new DirectMlRuntimeComponent(store));
    }

    private void Install(DirectMlRuntimeComponent component)
    {
        var folder = component.Folders().Single();
        Directory.CreateDirectory(folder);
        foreach (var file in DirectMlRuntimeRelease.Files)
        {
            File.WriteAllBytes(Path.Combine(folder, file.FileName), [1]);
        }

        component.Ready.ShouldBeTrue();
    }

    private sealed class Setting : IOcrAccelerationSetting
    {
        public OcrAcceleration Value { get; set; }
    }

    private sealed class Platform(string rid) : IToolPlatform
    {
        public string Rid => rid;

        public bool UseSystemPrograms => false;

        public string Executable(string name) => name;

        public Task ExtractAsync(string archive, string targetFolder, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<string>> MissingLibrariesAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<string>>([]);
    }

    private sealed class RefusingDownloader : IZipEntryDownloader
    {
        public Task DownloadAsync(ZipEntryArchive entry, string file, IProgress<ToolProgress>? progress, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Nothing is downloaded in this test.");
    }

    /// <summary>A network that spells its word (the recogniser) or finds the dark bars (the detector); it may fail after some runs.</summary>
    private sealed class Net(string word, int failAfter = int.MaxValue) : IOcrNetwork
    {
        private readonly LocalOcrTests.FakeDetector _detector = new();
        private int _runs;

        public OcrTensor Run(OcrTensor input)
        {
            if (++_runs > failAfter)
            {
                throw new InvalidOperationException("ONNX Runtime (DirectML): the device was removed");
            }

            return input.Shape[1] == 3 && input.Shape[2] == LocalOcrModels.RecognizerHeight ? LocalOcrTests.Output(word) : _detector.Run(input);
        }

        public void Dispose()
        {
        }
    }

    private sealed class Factory(string word) : IOcrNetworkFactory
    {
        public bool FailOpen { get; init; }

        /// <summary>Pages after which the networks fail (each page runs the detector and the recogniser once).</summary>
        public int FailRunAfter { get; init; } = int.MaxValue;

        public int Opened => Pairs.Count;

        public List<OcrNetworkPair> Pairs { get; } = [];

        public string Device => word;

        public OcrNetworkPair Open(string folder)
        {
            if (FailOpen)
            {
                throw new InvalidOperationException("The DirectML runtime could not be loaded: DirectML.dll");
            }

            var pair = new OcrNetworkPair(new Net(word, FailRunAfter), new Net(word));
            Pairs.Add(pair);
            return pair;
        }
    }

    /// <summary>Grants the card (or not) and counts the leases asked for and given back.</summary>
    private sealed class Coordinator : IGpuCoordinator
    {
        public bool Grant { get; init; } = true;

        public int Asked { get; private set; }

        public int Released { get; private set; }

        public ILocalModel? Loaded => null;

        public event EventHandler? Changed
        {
            add { }
            remove { }
        }

        public Task<IGpuLease?> TryAcquireAsync(IModel model, TimeSpan wait, CancellationToken cancellationToken)
        {
            Asked++;
            wait.ShouldBe(HybridOcrNetworkSource.CardWait);
            return Task.FromResult<IGpuLease?>(Grant ? new GpuLease(model, true, VramReading.None, _ =>
            {
                Released++;
                return ValueTask.CompletedTask;
            }) : null);
        }

        public Task<IGpuLease> AcquireAsync(IModel model, ModelMemoryNeed? need, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IGpuLease> AcquireAsync(CatalogModel model, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task FreeAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<IReadOnlyList<string>> RetryStuckAsync(IModel keep, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<string>>([]);
    }
}
