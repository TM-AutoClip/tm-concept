using TMConcept.Core.Hardware;

namespace TMConcept.Core.Music;

/// <summary>
/// Whether the local music model fits this computer (0.1.46, C5; the developer's rule of 23 Nov 2026: "Müzik modeli yalnız NVIDIA"). Pure:
/// the card report comes from <see cref="IGpuInfo"/>, the system from the caller.
/// </summary>
public static class LocalMusicSupport
{
    /// <summary>The local music model on a computer with <paramref name="report"/>; <paramref name="linux"/> = a Linux computer.</summary>
    public static LocalMusicCard Of(GpuReport report, bool linux)
    {
        ArgumentNullException.ThrowIfNull(report);
        return new LocalMusicCard(report.Adapters.Any(a => a.Vendor == GpuVendor.Nvidia), linux);
    }
}
