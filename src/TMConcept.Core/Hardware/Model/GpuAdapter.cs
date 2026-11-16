namespace TMConcept.Core.Hardware;

/// <summary>A graphics card of this computer (software and "basic render" adapters are never listed).</summary>
/// <param name="Vendor">Its maker.</param>
/// <param name="Name">Its name as the driver reports it (no serial number).</param>
/// <param name="VideoMemoryBytes">Its own (dedicated) memory in bytes; null when it could not be read.</param>
public sealed record GpuAdapter(GpuVendor Vendor, string Name, long? VideoMemoryBytes);
