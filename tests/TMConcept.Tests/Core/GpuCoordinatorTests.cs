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

    private static GpuCoordinator Coordinator(FakeVramProbe? probe = null, params ILocalModelSource[] sources) =>
        new(new ModelDirectory(sources), probe ?? new FakeVramProbe(new VramReading(16 * GiB, 14 * GiB, true, "test")), TestWaiters.Fast(), TimeProvider.System,
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

        probe.Reads.ShouldBeGreaterThanOrEqualTo(3, "the memory was read after the unload, before the image model got the card");
        changes.ShouldBe(3, "loaded, unloaded, loaded");

        // Back to the first model: the image model is unloaded (it has nothing loaded, but the call is safe) and the LLM loads again.
        await using (await gpu.AcquireAsync(llm, null, CancellationToken.None))
        {
            image.Unloads.ShouldBe(1);
            llm.Loads.ShouldBe(2);
        }
    }

    [Fact]
    public async Task AModelThatDoesNotFitGoesToMainMemoryWithoutAQuestionAndSaysSo()
    {
        // 8 GB free, a 12 GB model that places itself (a server: what does not fit goes to main memory): it goes ahead, slower, with a warning.
        var probe = new FakeVramProbe(new VramReading(16 * GiB, 8 * GiB, true, "test"));
        var notices = new Engines.ImageBackendTests.RecordingNotices();
        var handler = new ScriptedShortfallHandler(MemoryShortfallChoice.Cancel);
        var gpu = new GpuCoordinator(new ModelDirectory([]), probe, TestWaiters.Fast(handler), TimeProvider.System, NullLogger<GpuCoordinator>.Instance,
            notices: notices);
        await (await gpu.AcquireAsync(new FakeLocalModel("a"), null, CancellationToken.None)).DisposeAsync();
        var reads = probe.Reads;

        await using var lease = await gpu.AcquireAsync(new FakeLocalModel("b", videoGb: 12), null, CancellationToken.None);

        (probe.Reads - reads).ShouldBe(2, "one reading for the check, one for the lease");
        lease.Memory.FreeBytes.ShouldBe(8 * GiB);
        handler.Asked.ShouldBeEmpty();
        notices.Placed.ShouldBe([MemoryPlacement.MainMemory]);
    }

    [Fact]
    public async Task AfterFreeTheNextModelWaitsThreeSecondsBeforeItMeasures()
    {
        // The 0.1.17 incident path: the voice was freed (FreeAsync) and the image model's lease measured at once, with no wait at all.
        var clock = new CountingTimeProvider();
        var probe = new FakeVramProbe(new VramReading(6 * GiB, (long)(4.3 * GiB), true, "PDH"));
        var waiter = new MemoryWaiter(clock, MemoryWaitOptions.Default, NullLogger<MemoryWaiter>.Instance);
        var gpu = new GpuCoordinator(new ModelDirectory([]), probe, waiter, clock, NullLogger<GpuCoordinator>.Instance);
        await (await gpu.AcquireAsync(new FakeLocalModel("f5-tts"), null, CancellationToken.None)).DisposeAsync();
        await gpu.FreeAsync(CancellationToken.None);
        var reads = probe.Reads;

        var leasing = gpu.AcquireAsync(new FakeLocalModel("z-image-turbo", videoGb: 4), null, CancellationToken.None);
        await clock.AdvanceAfterTimerAsync(1, TimeSpan.FromSeconds(2.9));
        probe.Reads.ShouldBe(reads, "nothing is measured before 3 s");
        clock.Advance(TimeSpan.FromSeconds(0.1));
        await using var lease = await leasing.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        lease.OnCard.ShouldBeTrue();
        (probe.Reads - reads).ShouldBe(2);
    }

    [Fact]
    public async Task CancelInTheMemoryQuestionGivesTheCardBackAndNothingWaitsForTheQuestion()
    {
        // A voice that cannot leave weights off the card (its minimum is three quarters of its 8 GB class) finds 2 GiB free.
        var probe = new FakeVramProbe(new VramReading(16 * GiB, 2 * GiB, true, "PDH"));
        var handler = new ScriptedShortfallHandler(MemoryShortfallChoice.Cancel) { Hold = true };
        using var close = new CancellationTokenSource();
        var gpu = new GpuCoordinator(new ModelDirectory([]), probe, TestWaiters.Fast(handler), TimeProvider.System, NullLogger<GpuCoordinator>.Instance);
        var voice = new StrictModel("voxcpm2", 8);

        var asking = Task.Run(() => gpu.AcquireAsync(voice, null, close.Token), TestContext.Current.CancellationToken);
        await CountingTimeProvider.UntilAsync(() => handler.Asked.Count == 1);

        // While the question is open: freeing the card and leasing an online model never wait for it.
        await gpu.FreeAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await (await gpu.AcquireAsync(new OnlineModel(new CatalogModel { Provider = "openai", Id = "gpt" }, true), null, CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken)).DisposeAsync();
        handler.Asked[0].NeededVideoBytes.ShouldBe(8 * GiB / 4 * 3);
        handler.Asked[0].CardFreeBytes.ShouldBe(2 * GiB);

        // The queue's Cancel closes the question; the card is given back (no user count left), the next model gets it.
        await close.CancelAsync();
        await Should.ThrowAsync<OperationCanceledException>(() => asking);
        using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var next = await gpu.AcquireAsync(new FakeLocalModel("llm", videoGb: 1), null, limit.Token);
        next.OnCard.ShouldBeTrue();
        voice.Loads.ShouldBe(0, "a model whose memory is not free is never loaded");
    }

    [Fact]
    public async Task TheUsersCancelThrowsTheQueueStopAndRetryGoesOn()
    {
        var probe = new FakeVramProbe(new VramReading(16 * GiB, 2 * GiB, true, "PDH"));
        var cancel = new ScriptedShortfallHandler(MemoryShortfallChoice.Cancel);
        var gpu = new GpuCoordinator(new ModelDirectory([]), probe, TestWaiters.Fast(cancel), TimeProvider.System, NullLogger<GpuCoordinator>.Instance);
        await TestWaiters.CatchAsync<MemoryShortfallCanceledException>(() => gpu.AcquireAsync(new StrictModel("voxcpm2", 8), null, CancellationToken.None));

        // Retry after the user closed a game: 2 GiB, 2 GiB, then 12 GiB.
        var freed = new FakeVramProbe(new VramReading(16 * GiB, 2 * GiB, true, "PDH"), new VramReading(16 * GiB, 2 * GiB, true, "PDH"),
            new VramReading(16 * GiB, 12 * GiB, true, "PDH"));
        var retry = new ScriptedShortfallHandler(MemoryShortfallChoice.Retry);
        var again = new GpuCoordinator(new ModelDirectory([]), freed, TestWaiters.Fast(retry), TimeProvider.System, NullLogger<GpuCoordinator>.Instance);
        var voice = new StrictModel("voxcpm2", 8);
        await using var lease = await again.AcquireAsync(voice, null, CancellationToken.None);
        retry.Asked.Count.ShouldBe(1);
        voice.Loads.ShouldBe(1);
        lease.Memory.FreeBytes.ShouldBe(12 * GiB);
    }

    /// <summary>A model that must find three quarters of its class free on the card (as a PyTorch voice).</summary>
    private sealed class StrictModel(string id, int videoGb)
        : LocalModelBase(new CatalogModel { Provider = ModelProviders.Local, Id = id, Name = id, Capabilities = ModelCapability.Speech, MinVideoMemoryGb = videoGb })
    {
        public int Loads { get; private set; }

        public override ModelDevice Device => ModelDevice.Gpu;

        protected override long MinimumCardBytes(ModelMemoryNeed need) => need.VideoMemoryBytes / 4 * 3;

        protected override Task OnLoadAsync(CancellationToken cancellationToken)
        {
            Loads++;
            return Task.CompletedTask;
        }
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
    public async Task AFailedUnloadIsNotForgottenItIsAskedAgainLater()
    {
        var gpu = Coordinator();
        var stuck = new FakeLocalModel("server-model") { FailUnload = true };
        await (await gpu.AcquireAsync(stuck, null, CancellationToken.None)).DisposeAsync();

        // The next model still gets the card (it plans with the memory measured now), but the server model stays on the list.
        var image = new FakeLocalModel("image");
        var lease = await gpu.AcquireAsync(image, null, CancellationToken.None);
        lease.OnCard.ShouldBeTrue();
        stuck.Unloads.ShouldBe(1);
        stuck.IsLoaded.ShouldBeTrue("an unconfirmed unload leaves the model counted as loaded");
        await lease.DisposeAsync();

        stuck.FailUnload = false;
        await gpu.FreeAsync(CancellationToken.None);
        image.Unloads.ShouldBe(1);
        stuck.Unloads.ShouldBe(2, "freeing the card asks the model that would not unload once more");
        stuck.IsLoaded.ShouldBeFalse();

        await gpu.FreeAsync(CancellationToken.None);
        stuck.Unloads.ShouldBe(2, "once gone, it is not asked again");
    }

    [Fact]
    public async Task AStopWhileTheMemoryIsReadLeavesNoUserBehind()
    {
        var probe = new CancelingProbe();
        var gpu = new GpuCoordinator(new ModelDirectory([]), probe, TestWaiters.Fast(), TimeProvider.System, NullLogger<GpuCoordinator>.Instance);
        var llm = new FakeLocalModel("llm");

        // Stop pressed during the short memory reading after the load (nvidia-smi runs with the caller's token).
        await Should.ThrowAsync<OperationCanceledException>(() => gpu.AcquireAsync(llm, null, CancellationToken.None));

        // 0.1.17 kept a user count without a lease: every other local model waited for good. Now the next one gets the card at once.
        using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var lease = await gpu.AcquireAsync(new FakeLocalModel("image"), null, limit.Token);
        lease.OnCard.ShouldBeTrue();
        llm.Unloads.ShouldBe(1, "the loaded model was still known and was unloaded first");
    }

    [Fact]
    public async Task WhatServersHoldFromBeforeIsSweptOnceBeforeTheFirstCardLease()
    {
        var sweep = new CountingSweep();
        var gpu = new GpuCoordinator(new ModelDirectory([]), new FakeVramProbe(new VramReading(16 * GiB, 14 * GiB, true, "test")), TestWaiters.Fast(),
            TimeProvider.System, NullLogger<GpuCoordinator>.Instance, [sweep]);
        var llm = new FakeLocalModel("llm");

        await (await gpu.AcquireAsync(new OnlineModel(new CatalogModel { Provider = "openai", Id = "gpt" }, true), null, CancellationToken.None)).DisposeAsync();
        sweep.Kept.ShouldBeEmpty("an online model takes no card");

        await (await gpu.AcquireAsync(llm, null, CancellationToken.None)).DisposeAsync();
        await (await gpu.AcquireAsync(new FakeLocalModel("image"), null, CancellationToken.None)).DisposeAsync();
        await gpu.FreeAsync(CancellationToken.None);
        await (await gpu.AcquireAsync(llm, null, CancellationToken.None)).DisposeAsync();

        sweep.Kept.ShouldBe([llm.Key], "once per run, keeping the model about to be used");
    }

    private sealed class CancelingProbe : IVramProbe
    {
        private int _reads;

        public Task<VramReading> MeasureAsync(GpuAdapter? adapter, CancellationToken cancellationToken) =>
            ++_reads == 1 ? throw new OperationCanceledException() : Task.FromResult(new VramReading(16 * GiB, 14 * GiB, true, "test"));
    }

    private sealed class CountingSweep : IResidentModelSweep
    {
        public List<string> Kept { get; } = [];

        public Task SweepAsync(IModel keep, CancellationToken cancellationToken)
        {
            Kept.Add(keep.Key);
            return Task.CompletedTask;
        }
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