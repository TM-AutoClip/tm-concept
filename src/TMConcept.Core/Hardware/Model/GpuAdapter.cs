namespace TMConcept.Core.Hardware;

/// <summary>A graphics card of this computer (software and "basic render" adapters are never listed).</summary>
/// <param name="Vendor">Its maker.</param>
/// <param name="Name">Its name as the driver reports it (no serial number).</param>
/// <param name="VideoMemoryBytes">Its own (dedicated) memory in bytes; null when it could not be read.</param>
/// <param name="Key">
/// Where the card is found again for a live memory reading (<see cref="IVramProbe"/>): on Windows its adapter LUID as the performance counter
/// names it (<c>luid_0x00000000_0x0000d1a2</c>, lower case), on Linux its sysfs device folder (<c>/sys/class/drm/card1/device</c>); empty when
/// the source that listed it gives none.
/// </param>
public sealed record GpuAdapter(GpuVendor Vendor, string Name, long? VideoMemoryBytes, string Key = "");