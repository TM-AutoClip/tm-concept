using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using TMConcept.Core.Hardware;
using TMConcept.Core.Models;

namespace TMConcept.Tests.Core;

/// <summary>
/// One local model on the graphics card at a time: a lease for another local model unloads the one before and waits for its memory, an
/// online or processor model never waits, the same model can be leased twice, a flow cannot hold two different card models. Fake models and
/// a fake memory probe: nothing native, no graphics card.
/// </summary>
public sealed class GpuCoordinatorTests
{
    private const long GiB = 1024L * 1024 * 1024;
    private static readonly GpuCoordinatorOptions Fast = new(TimeSpan.FromMilliseconds(1), TimeSpan.FromSeconds(5));

    private static GpuCoordinator Coordinator(FakeVramProbe? probe = null, params ILocalModelSource[] sources) =>
        new(new ModelDirectory(sources), probe ?? new FakeVramProbe(new VramReading(16 * GiB, 14 * GiB, true, "test")), TimeProvider.System, Fast,
            NullLogger<GpuCoordinator>.Instance);

    [Fact]
    public async Task OnlineAndProcessorModelsNeverTakeTheCard()
    {
        var gpu = Coordinator();
        var onCard = new FakeLocalModel("llm");
        await using (await gpu.AcquireAsync(onCard, null, CancellationToken.None))
        {
            // While a card model is in use, an online model and a processor model still get their lease at once.
            var online = await gpu.AcquireAsync(new OnlineModel(new CatalogModel { Provider = "anthropic", Id = "claude" }, true), null, CancellationToken.None);
            online.OnCard.ShouldBeFalse();
            online.Memory.ShouldBe(VramReading.None);
            await online.DisposeAsync();

            var voice = new FakeLocalModel("kokoro", ModelDevice.Cpu);
            await using var cpu = await gpu.AcquireAsync(voice, null, CancellationToken.None);
            cpu.OnCard.ShouldBeFalse();
            voice.Loads.ShouldBe(0, "a processor model is not put on the card");
        }

        onCard.Unloads.ShouldBe(0);
        gpu.Loaded.ShouldBe(onCard, "the model stays on the card after its lease");
    }

    [Fact]
    public async Task TheNextLocalModelUnloadsThePreviousOneFirst()
    {
        var probe = new FakeVramProbe(new VramReading(16 * GiB, 6 * GiB, true, "test"), new VramReading(16 * GiB, 9 * GiB, true, "test"),
            new VramReading(16 * GiB, 14 * GiB, true, "test"));
        var gpu = Coordinator(probe);
        var llm = new FakeLocalModel("llm");
        var image = new FakeLocalModel("z-image", videoGb: 12);
        var changes = 0;
        gpu.Changed += (_, _) => changes++;

        await using (var first = await gpu.AcquireAsync(llm, null, CancellationToken.None))
        {
            first.OnCard.ShouldBeTrue();
            llm.Loads.ShouldBe(1);
        }

        await using (var second = await gpu.AcquireAsync(image, null, CancellationToken.None))
        {
            llm.Unloads.ShouldBe(1, "the prompt model leaves the card before the image model comes");
            image.Loads.ShouldBe(1);
            second.Memory.FreeBytes.ShouldBe(14 * GiB, "the memory after the unload is what the image model may use");
            gpu.Loaded.ShouldBe(image);
        }

        probe.Reads.ShouldBeGreaterThanOrEqualTo(3, "the memory was read until it reached the image model's need");
        changes.ShouldBe(3, "loaded, unloaded, loaded");

        // Back to the first model: the image model is unloaded (it has nothing loaded, but the call is safe) and the LLM loads again.
        await using (await gpu.AcquireAsync(llm, null, CancellationToken.None))
        {
            image.Unloads.ShouldBe(1);
            llm.Loads.ShouldBe(2);
        }
    }

    [Fact]
    public async Task TheWaitEndsWhenTheMemoryStopsRising()
    {
        // 8 GB free, then 8 GB again: the driver gave back all it will; the 12 GB model goes ahead (its weights move to main memory).
        var probe = new FakeVramProbe(new VramReading(16 * GiB, 8 * GiB, true, "test"));
        var gpu = Coordinator(probe);
        await (await gpu.AcquireAsync(new FakeLocalModel("a"), null, CancellationToken.None)).DisposeAsync();
        var reads = probe.Reads;

        await using var lease = await gpu.AcquireAsync(new FakeLocalModel("b", videoGb: 12), null, CancellationToken.None);

        (probe.Reads - reads).ShouldBe(3, "two readings to see it stop rising, one for the lease");
        lease.Memory.FreeBytes.ShouldBe(8 * GiB);
    }

    [Fact]
    public async Task AnEstimateIsNotWaitedFor()
    {
        var probe = new FakeVramProbe(new VramReading(16 * GiB, VramReading.Estimate(16 * GiB), false, "estimate"));
        var gpu = Coordinator(probe);
        await (await gpu.AcquireAsync(new FakeLocalModel("a"), null, CancellationToken.None)).DisposeAsync();
        var reads = probe.Reads;

        await (await gpu.AcquireAsync(new FakeLocalModel("b", videoGb: 30), null, CancellationToken.None)).DisposeAsync();

        (probe.Reads - reads).ShouldBe(2, "one reading after the unload, one for the lease");
    }

    [Fact]
    public async Task AnotherModelWaitsUntilTheModelInUseIsReleased()
    {
        var gpu = Coordinator();
        var llm = new FakeLocalModel("llm");
        var image = new FakeLocalModel("image");
        var held = await Task.Run(() => gpu.AcquireAsync(llm, null, CancellationToken.None));

        var waiting = Task.Run(() => gpu.AcquireAsync(image, null, CancellationToken.None));
        await Task.Delay(100, TestContext.Current.CancellationToken);
        waiting.IsCompleted.ShouldBeFalse("the card is in use by another page's model");
        llm.Unloads.ShouldBe(0, "a model in use is never unloaded");

        await held.DisposeAsync();
        await using var granted = await waiting.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        granted.Model.ShouldBe(image);
        llm.Unloads.ShouldBe(1);
    }

    [Fact]
    public async Task TheSameModelCanBeLeasedByTwoFlowsAtOnce()
    {
        var gpu = Coordinator();
        var llm = new FakeLocalModel("llm");
        var first = await Task.Run(() => gpu.AcquireAsync(llm, null, CancellationToken.None));
        var second = await Task.Run(() => gpu.AcquireAsync(llm, null, CancellationToken.None)).WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        llm.Loads.ShouldBe(1);
        await first.DisposeAsync();
        await second.DisposeAsync();
        await first.DisposeAsync();
        llm.Unloads.ShouldBe(0);

        // Nested in one flow (an image loop leases each image again inside the loop's lease).
        await using (await gpu.AcquireAsync(llm, null, CancellationToken.None))
        {
            await using var inner = await gpu.AcquireAsync(llm, null, CancellationToken.None);
            inner.OnCard.ShouldBeTrue();
        }
    }

    [Fact]
    public async Task AFlowCannotHoldTwoCardModels()
    {
        var gpu = Coordinator();
        await using var held = await gpu.AcquireAsync(new FakeLocalModel("llm"), null, CancellationToken.None);

        var error = await Should.ThrowAsync<InvalidOperationException>(() => gpu.AcquireAsync(new FakeLocalModel("image"), null, CancellationToken.None));
        error.Message.ShouldContain("local/llm");
    }

    [Fact]
    public async Task AWaitingLeaseCanBeCanceled()
    {
        var gpu = Coordinator();
        var held = await Task.Run(() => gpu.AcquireAsync(new FakeLocalModel("llm"), null, CancellationToken.None));
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Should.ThrowAsync<OperationCanceledException>(() => Task.Run(() => gpu.AcquireAsync(new FakeLocalModel("image"), null, cancel.Token)));
        await held.DisposeAsync();
    }

    [Fact]
    public async Task AFailedUnloadIsLoggedAndTheCardIsTakenAnyway()
    {
        var gpu = Coordinator();
        var stuck = new FakeLocalModel("server-model") { FailUnload = true };
        await (await gpu.AcquireAsync(stuck, null, CancellationToken.None)).DisposeAsync();

        await using var lease = await gpu.AcquireAsync(new FakeLocalModel("image"), null, CancellationToken.None);

        lease.OnCard.ShouldBeTrue();
        stuck.Unloads.ShouldBe(1);
    }

    [Fact]
    public async Task FreeUnloadsAnIdleModelOnly()
    {
        var gpu = Coordinator();
        var llm = new FakeLocalModel("llm");
        var lease = await gpu.AcquireAsync(llm, null, CancellationToken.None);

        await gpu.FreeAsync(CancellationToken.None);
        llm.Unloads.ShouldBe(0, "in use");

        await lease.DisposeAsync();
        await gpu.FreeAsync(CancellationToken.None);
        llm.Unloads.ShouldBe(1);
        gpu.Loaded.ShouldBeNull();
        llm.IsLoaded.ShouldBeFalse();
    }

    [Fact]
    public async Task ACatalogEntryIsLeasedThroughItsModel()
    {
        var image = new FakeLocalModel("z-image");
        var gpu = Coordinator(null, new FixedSource(image));

        await using var lease = await gpu.AcquireAsync(image.Catalog, CancellationToken.None);
        lease.Model.ShouldBe(image);
        lease.OnCard.ShouldBeTrue();

        await using var other = await gpu.AcquireAsync(new CatalogModel { Provider = ModelProviders.Local, Id = "unknown", Capabilities = ModelCapability.Speech },
            CancellationToken.None);
        other.Model.ShouldBeOfType<ProcessorModel>("a local entry no source knows runs on the processor");
        other.OnCard.ShouldBeFalse();
    }

    private sealed class FixedSource(ILocalModel model) : ILocalModelSource
    {
        public ILocalModel? Create(CatalogModel catalog) => catalog.Id == model.Id ? model : null;
    }
}