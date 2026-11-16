using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using TMConcept.Core.Hardware;
using TMConcept.Engines.Hardware;
using TMConcept.Tests.Desktop;

namespace TMConcept.Tests.Engines;

/// <summary>Free graphics card memory: the smallest figure of the sources, an estimate when none can read it, and the sources themselves.</summary>
public sealed class VramProbeTests
{
    private const long GiB = 1024L * 1024 * 1024;
    private static readonly GpuAdapter Card = new(GpuVendor.Amd, "AMD Radeon RX 7800 XT", 16 * GiB, "luid_0x00000000_0x0000d1a2");

    [Fact]
    public async Task TheSmallestFreeFigureWins()
    {
        var probe = new VramProbe(new FakeGpu(new GpuReport([Card], false, true)),
            [new FixedSource("PDH", 13 * GiB), new FixedSource("DXGI budget", 11 * GiB), new FixedSource("none", null)], NullLogger<VramProbe>.Instance);

        var reading = await probe.MeasureAsync(null, CancellationToken.None);

        reading.ShouldBe(new VramReading(16 * GiB, 11 * GiB, true, "DXGI budget"));
    }

    [Fact]
    public async Task NothingMeasurableGivesTheEstimateAndABrokenSourceIsSkipped()
    {
        var probe = new VramProbe(new FakeGpu(new GpuReport([Card], false, true)), [new FixedSource("broken", null, fail: true)], NullLogger<VramProbe>.Instance);

        var reading = await probe.MeasureAsync(Card, CancellationToken.None);

        reading.Measured.ShouldBeFalse();
        reading.FreeBytes.ShouldBe(VramReading.Estimate(16 * GiB));
        reading.Source.ShouldBe("estimate");
    }

    [Fact]
    public async Task NoCardOrUnknownMemoryGivesNone()
    {
        var probe = new VramProbe(new FakeGpu(GpuReport.None), [new FixedSource("PDH", GiB)], NullLogger<VramProbe>.Instance);
        (await probe.MeasureAsync(null, CancellationToken.None)).ShouldBe(VramReading.None);
        (await probe.MeasureAsync(Card with { VideoMemoryBytes = null }, CancellationToken.None)).ShouldBe(VramReading.None);
    }

    [Fact]
    public async Task AmdSysfsFilesGiveTheFreeMemory()
    {
        var root = Path.Combine(Path.GetTempPath(), "tmconcept-tests", "sysfs-" + Guid.NewGuid().ToString("N"));
        var device = Path.Combine(root, "card1", "device");
        Directory.CreateDirectory(device);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(device, "vendor"), "0x1002\n", TestContext.Current.CancellationToken);
            await File.WriteAllTextAsync(Path.Combine(device, "class"), "0x030000\n", TestContext.Current.CancellationToken);
            await File.WriteAllTextAsync(Path.Combine(device, "mem_info_vram_total"), (16 * GiB).ToString(System.Globalization.CultureInfo.InvariantCulture), TestContext.Current.CancellationToken);
            await File.WriteAllTextAsync(Path.Combine(device, "mem_info_vram_used"), (3 * GiB).ToString(System.Globalization.CultureInfo.InvariantCulture), TestContext.Current.CancellationToken);
            var sysfs = new SysfsAdapterSource(root, NullLogger<SysfsAdapterSource>.Instance);

            var card = sysfs.Read().ShouldHaveSingleItem();
            card.Key.ShouldBe(device);
            (await sysfs.FreeBytesAsync(card, CancellationToken.None)).ShouldBe(13 * GiB);
            (await sysfs.FreeBytesAsync(Card, CancellationToken.None)).ShouldBeNull("a Windows key is not a sysfs folder");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void TheLuidIsNamedAsThePerformanceCounterNamesIt()
    {
        DxgiAdapterSource.LuidKey(0, 0xD1A2).ShouldBe("luid_0x00000000_0x0000d1a2");
        DxgiAdapterSource.LuidKey(1, 0xFFFFFFFF).ShouldBe("luid_0x00000001_0xffffffff");
    }

    [Fact]
    public async Task TheWindowsSourcesNeverThrow()
    {
        // Reads this computer's counters and DXGI (no device is created, nothing runs on a card); any machine may have no card at all.
        var dxgi = new DxgiAdapterSource(NullLogger<DxgiAdapterSource>.Instance);
        var pdh = new PdhVideoMemorySource(NullLogger<PdhVideoMemorySource>.Instance);
        foreach (var card in dxgi.Read().Append(Card))
        {
            var budget = await ((IVideoMemorySource)dxgi).FreeBytesAsync(card, CancellationToken.None);
            var counted = await pdh.FreeBytesAsync(card, CancellationToken.None);
            (budget ?? 0).ShouldBeGreaterThanOrEqualTo(0);
            (counted ?? 0).ShouldBeInRange(0, card.VideoMemoryBytes ?? 0);
            if (!OperatingSystem.IsWindows())
            {
                budget.ShouldBeNull();
                counted.ShouldBeNull();
            }
        }
    }

    private sealed class FixedSource(string name, long? free, bool fail = false) : IVideoMemorySource
    {
        public string Name { get; } = name;

        public Task<long?> FreeBytesAsync(GpuAdapter adapter, CancellationToken cancellationToken) =>
            fail ? throw new InvalidOperationException("broken") : Task.FromResult(free);
    }
}