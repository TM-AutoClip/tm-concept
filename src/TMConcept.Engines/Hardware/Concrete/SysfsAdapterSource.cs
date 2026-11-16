using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using TMConcept.Core.Hardware;

namespace TMConcept.Engines.Hardware;

/// <summary>
/// Linux: the graphics cards under <c>/sys/class/drm/card&lt;n&gt;/device/</c> (readable by every user): the maker from <c>vendor</c>
/// (<c>0x1002</c>), the memory of an AMD card from <c>mem_info_vram_total</c> (bytes; the amdgpu driver). NVIDIA's driver writes no memory
/// file here (nvidia-smi fills it in), Intel's integrated cards have no own memory. Connector entries (<c>card0-HDMI-A-1</c>) are skipped;
/// the PCI class must be a display controller (0x03xxxx). A card's key is its device folder; as a memory source it reads the amdgpu files
/// <c>mem_info_vram_used</c> and <c>mem_info_vram_total</c> there (bytes, the whole system).
/// </summary>
public sealed partial class SysfsAdapterSource : IGraphicsAdapterSource, IVideoMemorySource
{
    private readonly string _root;
    private readonly ILogger<SysfsAdapterSource> _logger;

    /// <summary>The system's <c>/sys/class/drm</c>.</summary>
    public SysfsAdapterSource(ILogger<SysfsAdapterSource> logger)
        : this("/sys/class/drm", logger)
    {
    }

    /// <summary>Another folder laid out like <c>/sys/class/drm</c> (tests).</summary>
    internal SysfsAdapterSource(string root, ILogger<SysfsAdapterSource> logger)
    {
        _root = root;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "sysfs";

    /// <inheritdoc />
    public IReadOnlyList<GpuAdapter> Read()
    {
        var list = new List<GpuAdapter>();
        try
        {
            if (!Directory.Exists(_root))
            {
                return list;
            }

            foreach (var card in Directory.GetDirectories(_root).Where(d => Card().IsMatch(Path.GetFileName(d))).Order(StringComparer.Ordinal))
            {
                var device = Path.Combine(card, "device");
                var pciClass = Text(Path.Combine(device, "class"));
                if (Text(Path.Combine(device, "vendor")) is not { } vendorId || (pciClass is not null && !pciClass.StartsWith("0x03", StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                var vendor = PciVendors.Vendor(vendorId);
                var deviceId = Text(Path.Combine(device, "device")) ?? string.Empty;
                long? memory = long.TryParse(Text(Path.Combine(device, "mem_info_vram_total")), NumberStyles.None, CultureInfo.InvariantCulture, out var bytes)
                    && bytes > 0 ? bytes : null;
                var name = PciVendors.Label(vendor) + (deviceId.Length > 0 ? " (" + deviceId.Replace("0x", string.Empty, StringComparison.OrdinalIgnoreCase) + ")" : string.Empty);
                list.Add(new GpuAdapter(vendor, name, memory, device));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "The graphics cards could not be read from sysfs");
        }

        return list;
    }

    /// <inheritdoc />
    public Task<long?> FreeBytesAsync(GpuAdapter adapter, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        if (adapter.Key.Length == 0 || !adapter.Key.StartsWith(_root, StringComparison.Ordinal))
        {
            return Task.FromResult<long?>(null);
        }

        var used = Number(Path.Combine(adapter.Key, "mem_info_vram_used"));
        var total = Number(Path.Combine(adapter.Key, "mem_info_vram_total")) ?? adapter.VideoMemoryBytes;
        return Task.FromResult<long?>(used is { } u && total is { } t && t > 0 ? Math.Max(0, t - u) : null);
    }

    private static long? Number(string file) =>
        long.TryParse(Text(file), NumberStyles.None, CultureInfo.InvariantCulture, out var value) ? value : null;

    private static string? Text(string file)
    {
        try
        {
            return File.Exists(file) ? File.ReadAllText(file).Trim() : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    [GeneratedRegex(@"^card\d+$")]
    private static partial Regex Card();
}
