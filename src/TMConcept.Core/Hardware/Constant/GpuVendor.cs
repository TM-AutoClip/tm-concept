namespace TMConcept.Core.Hardware;

/// <summary>The maker of a graphics card (from its PCI vendor id).</summary>
public enum GpuVendor
{
    /// <summary>Another maker or not known.</summary>
    Other = 0,

    /// <summary>NVIDIA (PCI vendor 0x10DE).</summary>
    Nvidia = 1,

    /// <summary>AMD (PCI vendor 0x1002, rarely 0x1022).</summary>
    Amd = 2,

    /// <summary>Intel (PCI vendor 0x8086).</summary>
    Intel = 3,
}
