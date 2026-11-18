using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using TMConcept.Core.Hardware;
using TMConcept.Core.Models;

namespace TMConcept.Tests.Core;

/// <summary>
/// 0.1.27: <see cref="IGpuCoordinator.TryAcquireAsync"/>, the card for one page of the local OCR. It never waits long, never asks the user,
/// and leaves nothing behind when it gives up: a busy card, too little memory or another model held by the same flow answer null (the page is
/// read on the processor). Fake models and a fake memory probe: nothing native, no graphics card.
/// </summary>
public sealed class GpuTryAcquireTests
{
    private const long GiB = 1024L * 1024 * 1024;

    private static GpuCoordinator Coordinator(FakeVramProbe? probe = null, IMemoryShortfallHandler? handler = null) =>
        new(new ModelDirectory([]), probe ?? new FakeVramProbe(new VramReading(16 * GiB, 14 * GiB, true, "test")), TestWaiters.Fast(handler),
            TimeProvider.System, NullLogger<GpuCoordinator>.Instance);

    [Fact]
    public async Task AFreeCardIsLeasedAtOnce()
    {
        var gpu = Coordinator();
        var ocr = new FakeLocalModel("pp-ocr-latin", videoGb: 1);
        await using (var lease = await gpu.TryAcquireAsync(ocr, TimeSpan.FromSeconds(2), CancellationToken.None))
        {
            lease.ShouldNotBeNull();
            lease.OnCard.ShouldBeTrue();
            ocr.Loads.ShouldBe(1);
            gpu.Loaded.ShouldBe(ocr);
        }

        // The second page: the model is on the card and checked, the lease comes without another reading.
        await using var again = await gpu.TryAcquireAsync(ocr, TimeSpan.FromSeconds(2), CancellationToken.None);
        again.ShouldNotBeNull();
        ocr.Loads.ShouldBe(1);
    }

    [Fact]
    public async Task ABusyCardAnswersNullAfterTheWaitAndLeavesNoUserBehind()
    {
        var gpu = Coordinator();
        var video = new FakeLocalModel("z-image");
        var ocr = new FakeLocalModel("pp-ocr-latin", videoGb: 1);
        // Another flow (the video queue) holds the card.
        var held = await Task.Run(() => gpu.AcquireAsync(video, null, CancellationToken.None));
        var page = await gpu.TryAcquireAsync(ocr, TimeSpan.FromMilliseconds(50), CancellationToken.None);
        page.ShouldBeNull("the card is in use: the page goes to the processor");
        video.Unloads.ShouldBe(0, "the model in use is never unloaded");
        ocr.Loads.ShouldBe(0);
        await held.DisposeAsync();

        // Nothing was left counted: the video model can be leased again and another model unloads it as usual.
        var other = new FakeLocalModel("llm");
        await using (await gpu.AcquireAsync(other, null, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken))
        {
            video.Unloads.ShouldBe(1);
        }
    }

    [Fact]
    public async Task AnIdleModelOnTheCardIsUnloadedForThePage()
    {
        var gpu = Coordinator();
        var llm = new FakeLocalModel("llm");
        var ocr = new FakeLocalModel("pp-ocr-latin", videoGb: 1);
        await (await gpu.AcquireAsync(llm, null, CancellationToken.None)).DisposeAsync();

        await using var page = await gpu.TryAcquireAsync(ocr, TimeSpan.FromSeconds(2), CancellationToken.None);
        page.ShouldNotBeNull();
        llm.Unloads.ShouldBe(1, "one model on the card at a time");
        gpu.Loaded.ShouldBe(ocr);
    }

    [Fact]
    public async Task TooLittleMemoryAnswersNullWithoutAskingTheUser()
    {
        var handler = new CountingHandler();
        var gpu = Coordinator(new FakeVramProbe(new VramReading(8 * GiB, GiB / 4, true, "test")), handler);
        var ocr = new FakeLocalModel("pp-ocr-latin", videoGb: 1);

        var page = await gpu.TryAcquireAsync(ocr, TimeSpan.FromSeconds(2), CancellationToken.None);

        page.ShouldBeNull();
        handler.Asked.ShouldBe(0, "a page never opens the memory question");
        ocr.Loads.ShouldBe(0);

        // The reservation was given back: another model gets the card at once.
        var other = new FakeLocalModel("llm", videoGb: 0);
        await using var lease = await gpu.AcquireAsync(other, null, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        lease.OnCard.ShouldBeTrue();
    }

    [Fact]
    public async Task AFlowThatHoldsAnotherModelGetsNullInsteadOfAnException()
    {
        var gpu = Coordinator();
        var llm = new FakeLocalModel("llm");
        var ocr = new FakeLocalModel("pp-ocr-latin", videoGb: 1);
        await using var held = await gpu.AcquireAsync(llm, null, CancellationToken.None);

        var page = await gpu.TryAcquireAsync(ocr, TimeSpan.FromSeconds(2), CancellationToken.None);

        page.ShouldBeNull();

        // The rule of two models in one flow still throws for a normal lease.
        await Should.ThrowAsync<InvalidOperationException>(() => gpu.AcquireAsync(ocr, null, CancellationToken.None));
    }

    [Fact]
    public async Task TheCallersCancelThrowsWhileTheTimeOutAnswersNull()
    {
        var gpu = Coordinator();
        var video = new FakeLocalModel("z-image");
        var ocr = new FakeLocalModel("pp-ocr-latin", videoGb: 1);
        await using var held = await Task.Run(() => gpu.AcquireAsync(video, null, CancellationToken.None));
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Should.ThrowAsync<OperationCanceledException>(() => Task.Run(() => gpu.TryAcquireAsync(ocr, TimeSpan.FromSeconds(30), cancel.Token)));
        (await Task.Run(() => gpu.TryAcquireAsync(ocr, TimeSpan.FromMilliseconds(20), CancellationToken.None))).ShouldBeNull();
    }

    [Fact]
    public async Task AProcessorModelGetsAnOffCardLease()
    {
        var gpu = Coordinator();
        var ocr = new FakeLocalModel("pp-ocr-latin", ModelDevice.Cpu);
        await using var lease = await gpu.TryAcquireAsync(ocr, TimeSpan.Zero, CancellationToken.None);
        lease.ShouldNotBeNull();
        lease.OnCard.ShouldBeFalse();
    }

    private sealed class CountingHandler : IMemoryShortfallHandler
    {
        public int Asked { get; private set; }

        public Task<MemoryShortfallChoice> AskAsync(MemoryShortfall shortfall, CancellationToken cancellationToken)
        {
            Asked++;
            return Task.FromResult(MemoryShortfallChoice.Cancel);
        }
    }
}
