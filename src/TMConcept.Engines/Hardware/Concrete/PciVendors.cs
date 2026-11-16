using System.Globalization;
using TMConcept.Core.Hardware;

namespace TMConcept.Engines.Hardware;

/// <summary>PCI vendor ids of graphics card makers (the same numbers on Windows and Linux).</summary>
public static class PciVendors
{
    /// <summary>The maker of a PCI vendor id (0x10DE NVIDIA, 0x1002 / 0x1022 AMD, 0x8086 Intel).</summary>
    public static GpuVendor Vendor(uint id) => id switch
    {
        0x10DE => GpuVendor.Nvidia,
        0x1002 or 0x1022 => GpuVendor.Amd,
        0x8086 => GpuVendor.Intel,
        _ => GpuVendor.Other,
    };

    /// <summary>The maker of a hexadecimal vendor id as sysfs writes it (<c>0x1002</c>) or a registry device id carries it (<c>1002</c>).</summary>
    public static GpuVendor Vendor(string hex)
    {
        var text = (hex ?? string.Empty).Trim();
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            text = text[2..];
        }

        return uint.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var id) ? Vendor(id) : GpuVendor.Other;
    }

    /// <summary>The maker's name for a card whose own name is not known.</summary>
    public static string Label(GpuVendor vendor) => vendor switch
    {
        GpuVendor.Nvidia => "NVIDIA",
        GpuVendor.Amd => "AMD",
        GpuVendor.Intel => "Intel",
        _ => "Graphics card",
    };
}
