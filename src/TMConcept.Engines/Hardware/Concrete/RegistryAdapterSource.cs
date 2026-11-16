using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using TMConcept.Core.Hardware;

namespace TMConcept.Engines.Hardware;

/// <summary>
/// Windows, after DXGI: the display adapters' driver keys (<c>HKLM\SYSTEM\CurrentControlSet\Control\Class\{4d36e968-…}\0000</c>, <c>0001</c>
/// …), readable by a standard user. Memory from <c>HardwareInformation.qwMemorySize</c> (64-bit, written by current drivers for cards above
/// 4 GB), otherwise the older 32-bit <c>HardwareInformation.MemorySize</c>; the maker from <c>MatchingDeviceId</c> (<c>PCI\VEN_1002&amp;…</c>).
/// Only PCI devices are listed (the basic display driver is not). The key can keep removed cards, so it only fills in what DXGI did not give.
/// Each key is read on its own: the <c>Properties</c> sub key answers "access denied".
/// </summary>
public sealed partial class RegistryAdapterSource(ILogger<RegistryAdapterSource> logger) : IGraphicsAdapterSource
{
    private const string ClassKey = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";

    /// <inheritdoc />
    public string Name => "registry";

    /// <inheritdoc />
    public IReadOnlyList<GpuAdapter> Read()
    {
        if (!OperatingSystem.IsWindows())
        {
            return [];
        }

        var list = new List<GpuAdapter>();
        try
        {
            using var root = Registry.LocalMachine.OpenSubKey(ClassKey);
            if (root is null)
            {
                return list;
            }

            foreach (var name in root.GetSubKeyNames().Where(n => n.Length == 4 && n.All(char.IsAsciiDigit)))
            {
                try
                {
                    using var key = root.OpenSubKey(name);
                    if (key is not null && Adapter(key.GetValue("MatchingDeviceId") as string, key.GetValue("DriverDesc") as string,
                            key.GetValue("HardwareInformation.qwMemorySize"), key.GetValue("HardwareInformation.MemorySize")) is { } adapter)
                    {
                        list.Add(adapter);
                    }
                }
                catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
                {
                    logger.LogDebug(ex, "Display adapter key {Key} could not be read", name);
                }
            }
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            logger.LogWarning(ex, "The display adapter keys could not be read");
        }

        return list;
    }

    /// <summary>
    /// A card from one driver key's values (null when it is not a PCI device): <paramref name="deviceId"/> <c>PCI\VEN_10DE&amp;DEV_2504</c>,
    /// <paramref name="description"/> the driver's name of the card, the 64-bit memory value (a long or 8 bytes), else the 32-bit one (an
    /// int or 4 bytes).
    /// </summary>
    public static GpuAdapter? Adapter(string? deviceId, string? description, object? memory64, object? memory32)
    {
        if (deviceId is null || VendorId().Match(deviceId) is not { Success: true } vendor)
        {
            return null;
        }

        var memory = Bytes(memory64) ?? Bytes(memory32);
        var maker = PciVendors.Vendor(vendor.Groups[1].Value);
        var name = string.IsNullOrWhiteSpace(description) ? PciVendors.Label(maker) : description.Trim();
        return new GpuAdapter(maker, name, memory is > 0 ? memory : null);
    }

    private static long? Bytes(object? value) => value switch
    {
        long l => l,
        int i => (uint)i,
        byte[] { Length: >= 8 } b => BitConverter.ToInt64(b, 0),
        byte[] { Length: >= 4 } b => BitConverter.ToUInt32(b, 0),
        _ => null,
    };

    [GeneratedRegex(@"PCI\\VEN_([0-9A-Fa-f]{4})", RegexOptions.IgnoreCase)]
    private static partial Regex VendorId();
}
