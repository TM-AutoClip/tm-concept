using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using TMConcept.Core.Hardware;
using TMConcept.Engines.Hardware;
using TMConcept.Tests.Storage;

namespace TMConcept.Tests.Engines;

/// <summary>
/// Graphics card detection from its sources (sysfs fixture folders, nvidia-smi lines, registry values, fake sources and a fake runtime
/// probe): nothing native is loaded, no process is started.
/// </summary>
public sealed class GpuDetectionTests
{
    private const long GiB = 1024L * 1024 * 1024;

    [Theory]
    [InlineData(0x10DEu, GpuVendor.Nvidia)]
    [InlineData(0x1002u, GpuVendor.Amd)]
    [InlineData(0x1022u, GpuVendor.Amd)]
    [InlineData(0x8086u, GpuVendor.Intel)]
    [InlineData(0x1414u, GpuVendor.Other)]
    public void PciVendorIdsNameTheMaker(uint id, GpuVendor vendor)
    {
        PciVendors.Vendor(id).ShouldBe(vendor);
        PciVendors.Vendor("0x" + id.ToString("x4", System.Globalization.CultureInfo.InvariantCulture)).ShouldBe(vendor);
    }

    [Fact]
    public void SysfsListsDisplayCardsWithAmdMemoryAndSkipsConnectorsAndOtherDevices()
    {
        using var paths = new TempPaths();
        var root = Path.Combine(paths.DataDirectory, "drm");
        Card(root, "card0", "0x1002", "0x73bf", "0x030000", 17_163_091_968);
        Card(root, "card0-HDMI-A-1", "0x1002", "0x73bf", "0x030000", null);
        Card(root, "card1", "0x10de", "0x2504", "0x030200", null);
        Card(root, "card2", "0x8086", "0x1234", "0x040300", null);
        Directory.CreateDirectory(Path.Combine(root, "renderD128"));

        var cards = new SysfsAdapterSource(root, NullLogger<SysfsAdapterSource>.Instance).Read();

        cards.Count.ShouldBe(2);
        cards[0].ShouldBe(new GpuAdapter(GpuVendor.Amd, "AMD (73bf)", 17_163_091_968, Path.Combine(root, "card0", "device")), "the device folder is the key of its memory readings");
        cards[1].ShouldBe(new GpuAdapter(GpuVendor.Nvidia, "NVIDIA (2504)", null, Path.Combine(root, "card1", "device")));
    }

    [Fact]
    public void SysfsWithoutTheFolderFindsNothing()
    {
        using var paths = new TempPaths();
        new SysfsAdapterSource(Path.Combine(paths.DataDirectory, "missing"), NullLogger<SysfsAdapterSource>.Instance).Read().ShouldBeEmpty();
    }

    [Fact]
    public void NvidiaSmiLinesGiveNamesAndMemory() =>
        NvidiaSmiAdapterSource.Parse(["NVIDIA GeForce RTX 3060 Laptop GPU, 6144", "garbage", "NVIDIA RTX A6000, 49140", ", 12"])
            .ShouldBe([new GpuAdapter(GpuVendor.Nvidia, "NVIDIA GeForce RTX 3060 Laptop GPU", 6144L * 1024 * 1024),
                new GpuAdapter(GpuVendor.Nvidia, "NVIDIA RTX A6000", 49140L * 1024 * 1024)]);

    [Fact]
    public void RegistryValuesGiveTheMakerAndThe64BitMemoryFirst()
    {
        RegistryAdapterSource.Adapter(@"PCI\VEN_1002&DEV_73BF&SUBSYS_0E3A1002", "AMD Radeon RX 6800 XT", 16L * GiB, 0)
            .ShouldBe(new GpuAdapter(GpuVendor.Amd, "AMD Radeon RX 6800 XT", 16L * GiB));
        RegistryAdapterSource.Adapter(@"pci\ven_10de&dev_2504", "NVIDIA GeForce RTX 3060", null, BitConverter.GetBytes(unchecked((int)(4L * GiB - 1))))
            .ShouldBe(new GpuAdapter(GpuVendor.Nvidia, "NVIDIA GeForce RTX 3060", 4L * GiB - 1));
        RegistryAdapterSource.Adapter(@"PCI\VEN_8086&DEV_46A6", null, BitConverter.GetBytes(8L * GiB), null)
            .ShouldBe(new GpuAdapter(GpuVendor.Intel, "Intel", 8L * GiB));
        RegistryAdapterSource.Adapter(@"PCI\VEN_8086&DEV_46A6", "Intel Iris Xe", null, null)!.VideoMemoryBytes.ShouldBeNull();
        RegistryAdapterSource.Adapter(@"ROOT\BasicDisplay", "Microsoft Basic Display Adapter", 1L, 1).ShouldBeNull();
        RegistryAdapterSource.Adapter(null, "x", 1L, 1).ShouldBeNull();
    }

    [Fact]
    public void ALaterSourceFillsInOnlyTheMemoryThatIsMissing()
    {
        IReadOnlyList<GpuAdapter> sysfs = [new(GpuVendor.Nvidia, "NVIDIA (2504)", null), new(GpuVendor.Amd, "AMD (73bf)", 16 * GiB)];
        var merged = GpuDetector.Merge(sysfs, [new(GpuVendor.Nvidia, "NVIDIA GeForce RTX 3060", 12 * GiB), new(GpuVendor.Amd, "Other AMD", 1 * GiB)]);

        merged.ShouldBe([new GpuAdapter(GpuVendor.Nvidia, "NVIDIA GeForce RTX 3060", 12 * GiB), new GpuAdapter(GpuVendor.Amd, "AMD (73bf)", 16 * GiB)]);
        GpuDetector.Merge([], sysfs).ShouldBe(sysfs);

        // Two cards of one maker and no name match: nothing is guessed.
        IReadOnlyList<GpuAdapter> two = [new(GpuVendor.Nvidia, "A", null), new(GpuVendor.Nvidia, "B", null)];
        GpuDetector.Merge(two, [new(GpuVendor.Nvidia, "C", 8 * GiB)]).ShouldBe(two);
        GpuDetector.Merge(two, [new(GpuVendor.Nvidia, "b", 8 * GiB)])[1].VideoMemoryBytes.ShouldBe(8 * GiB);
    }

    [Fact]
    public void TheDetectorAsksLaterSourcesOnlyWhenNeededSurvivesFailuresAndLooksOnce()
    {
        var first = new FakeSource([new(GpuVendor.Amd, "AMD Radeon RX 7800 XT", 16 * GiB)]);
        var second = new FakeSource([new(GpuVendor.Amd, "never asked", 1)]);
        var probe = new FakeProbe(cuda: false, vulkan: true);
        var detector = new GpuDetector([new FakeSource(null), new FakeSource([]), first, second], probe, NullLogger<GpuDetector>.Instance);

        var report = detector.Detect();
        detector.Detect().ShouldBeSameAs(report);

        report.ShouldBe(new GpuReport(report.Adapters, false, true));
        report.Adapters.ShouldBe([new GpuAdapter(GpuVendor.Amd, "AMD Radeon RX 7800 XT", 16 * GiB)]);
        first.Calls.ShouldBe(1);
        second.Calls.ShouldBe(0, "every card's memory was known");
        probe.Calls.ShouldBe(1);
        GpuDetector.Describe(report).ShouldBe("AMD Radeon RX 7800 XT 16 GB");
        GpuDetector.Describe(GpuReport.None).ShouldBe("none");
    }

    [Fact]
    public void AProbeThatThrowsMeansNoRuntime()
    {
        var report = new GpuDetector([], new FakeProbe(cuda: true, vulkan: true) { Throws = true }, NullLogger<GpuDetector>.Instance).Detect();

        report.ShouldBe(new GpuReport(report.Adapters, false, false));
        report.Adapters.ShouldBeEmpty();
    }

    [Fact]
    public void TheLargestCardIsTheOneWithTheMostKnownMemory()
    {
        var report = new GpuReport([new(GpuVendor.Intel, "iGPU", null), new(GpuVendor.Amd, "dGPU", 16 * GiB), new(GpuVendor.Nvidia, "small", 4 * GiB)], true, true);

        report.Largest()!.Name.ShouldBe("dGPU");
        report.Largest(GpuVendor.Nvidia)!.Name.ShouldBe("small");
        report.Largest(GpuVendor.Other).ShouldBeNull();
    }

    [Fact]
    public void TheNativeSourcesNeverThrowOnThisComputer()
    {
        // Runs the real probes (read-only native calls; nothing is started on the graphics card). Only "does not throw" is checked: the
        // result depends on the computer.
        Should.NotThrow(() => new DxgiAdapterSource(NullLogger<DxgiAdapterSource>.Instance).Read());
        Should.NotThrow(() => new RegistryAdapterSource(NullLogger<RegistryAdapterSource>.Instance).Read());
        Should.NotThrow(() => new NativeRuntimeProbe().VulkanLoader());
    }

    private static void Card(string root, string name, string vendor, string device, string pciClass, long? memory)
    {
        var folder = Path.Combine(root, name, "device");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "vendor"), vendor + "\n");
        File.WriteAllText(Path.Combine(folder, "device"), device + "\n");
        File.WriteAllText(Path.Combine(folder, "class"), pciClass + "\n");
        if (memory is { } bytes)
        {
            File.WriteAllText(Path.Combine(folder, "mem_info_vram_total"), bytes.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\n");
        }
    }

    /// <summary>A source with fixed cards; null = it throws.</summary>
    private sealed class FakeSource(IReadOnlyList<GpuAdapter>? cards) : IGraphicsAdapterSource
    {
        public int Calls { get; private set; }

        public string Name => "fake";

        public IReadOnlyList<GpuAdapter> Read()
        {
            Calls++;
            return cards ?? throw new InvalidOperationException("broken source");
        }
    }

    private sealed class FakeProbe(bool cuda, bool vulkan) : IComputeRuntimeProbe
    {
        public int Calls { get; private set; }

        public bool Throws { get; init; }

        public bool CudaDriver()
        {
            Calls++;
            return Throws ? throw new DllNotFoundException("x") : cuda;
        }

        public bool VulkanLoader() => vulkan;

        public IReadOnlyList<string> MissingVisualCppRuntime() => [];
    }
}
