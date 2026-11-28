using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using TMConcept.Core.Hardware;
using TMConcept.Core.Models;
using TMConcept.Speech.Music;
using TMConcept.Tests.Core;

namespace TMConcept.Tests.Speech;

/// <summary>
/// 0.1.59 (K24-a): a local music model runs in a process of its own and cannot move its weights to main memory. With less than three quarters
/// of its class free it is not started: the memory question offers Try again, Cancel and "Try anyway". Fake memory readings, no card.
/// </summary>
public sealed class MusicMemoryTests
{
    private const long GiB = 1024L * 1024 * 1024;

    private static MusicLocalModel Yue() => new(new CatalogModel { Provider = ModelProviders.Local, Id = "yue-v1-exl2", Name = "YuE", MinVideoMemoryGb = 8, MemoryGb = 16 }, true);

    private static (GpuCoordinator Gpu, Engines.ImageBackendTests.RecordingNotices Notices) Coordinator(long freeBytes, ScriptedShortfallHandler handler)
    {
        var notices = new Engines.ImageBackendTests.RecordingNotices();
        var probe = new FakeVramProbe(new VramReading(6 * GiB, freeBytes, true, "test"));
        return (new GpuCoordinator(new ModelDirectory([]), probe, TestWaiters.Fast(handler, notices), TimeProvider.System, NullLogger<GpuCoordinator>.Instance,
            notices: notices), notices);
    }

    [Fact]
    public void TheModelIsPlannedOnTheCardFromItsMinimumAndNeverInMainMemory()
    {
        var yue = Yue();
        var full = yue.PlanMemory(new MemorySnapshot(new VramReading(12 * GiB, 9 * GiB, true, "test"), 30 * GiB), null);
        full.Placement.ShouldBe(MemoryPlacement.Card);

        var tight = yue.PlanMemory(new MemorySnapshot(new VramReading(8 * GiB, (long)(6.5 * GiB), true, "test"), 30 * GiB), null);
        tight.Placement.ShouldBe(MemoryPlacement.Card, "three quarters of the 8 GB class (6 GiB) is free");

        var spill = yue.PlanMemory(new MemorySnapshot(new VramReading(6 * GiB, (long)(3.8 * GiB), true, "test"), 30 * GiB), null);
        spill.Placement.ShouldBe(MemoryPlacement.SharedMemory, "the driver would spill into shared memory; never \"main memory, slower\"");
        spill.Runs.ShouldBeFalse();
        spill.CanTryAnyway.ShouldBeTrue();
        spill.NeededVideoBytes.ShouldBe(6 * GiB);

        new MusicLocalModel(new CatalogModel { Provider = ModelProviders.Local, Id = "x", MinVideoMemoryGb = 8 }, false)
            .PlanMemory(new MemorySnapshot(new VramReading(6 * GiB, GiB, true, "test"), null), null).Placement.ShouldBe(MemoryPlacement.Processor);
    }

    [Fact]
    public async Task TooLittleMemoryAsksFirstAndTryAnywayStartsItWithAWarning()
    {
        var handler = new ScriptedShortfallHandler(MemoryShortfallChoice.TryAnyway);
        var (gpu, notices) = Coordinator((long)(3.8 * GiB), handler);

        await using var lease = await gpu.AcquireAsync(Yue(), null, CancellationToken.None);

        lease.OnCard.ShouldBeTrue();
        var asked = handler.Asked.ShouldHaveSingleItem();
        asked.CanTryAnyway.ShouldBeTrue();
        asked.CardFreeBytes.ShouldBe((long)(3.8 * GiB));
        asked.NeededVideoBytes.ShouldBe(6 * GiB);
        notices.Placed.ShouldBe([MemoryPlacement.SharedMemory]);
    }

    [Fact]
    public async Task TryAgainMeasuresAgainAndCancelDoesNotStartIt()
    {
        var handler = new ScriptedShortfallHandler(MemoryShortfallChoice.Retry, MemoryShortfallChoice.Cancel);
        var (gpu, _) = Coordinator(2 * GiB, handler);

        await TestWaiters.CatchAsync<MemoryShortfallCanceledException>(() => gpu.AcquireAsync(Yue(), null, CancellationToken.None));

        handler.Asked.Count.ShouldBe(2, "Try again measured once more and asked again");
    }

    [Fact]
    public async Task TryAnywayIsIgnoredForAModelThatCannotRunAtAll()
    {
        // A model refused outright (a Python voice below its minimum) offers no "Try anyway": the answer counts as Cancel.
        var handler = new ScriptedShortfallHandler(MemoryShortfallChoice.TryAnyway);
        var waiter = TestWaiters.Fast(handler);
        var shortfall = new MemoryShortfall("voice", 6 * GiB, GiB, "test", 4 * GiB, null, 0);

        await TestWaiters.CatchAsync<MemoryShortfallCanceledException>(() =>
            waiter.WaitAsync(new FakeLocalModel("voice"), null, _ => Task.FromResult<MemoryShortfall?>(shortfall), CancellationToken.None));
    }
}
