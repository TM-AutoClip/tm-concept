using TMConcept.Core.Hardware;
using TMConcept.Core.Models;

namespace TMConcept.Core.Music;

/// <summary>
/// Whether a local music model fits this computer (0.1.46, C5; the developer's rule of 23 Nov 2026: "Müzik modeli yalnız NVIDIA"). Pure:
/// the card report comes from <see cref="IGpuInfo"/>, the system from the caller; what a model makes of it is <see cref="LocalMusicCard"/>'s
/// (red, tight) and the order of the lists <see cref="IModelListOrder"/>'s. Only a model that needs a graphics card
/// (<see cref="NeedsCard"/>: its catalog entry names a card class, ACE-Step 1.5 and every family of 28 Nov 2026, MusicGen to YuE) carries the card note; a model that runs on the processor does not.
/// </summary>
public static class LocalMusicSupport
{
    /// <summary>The local music models on a computer with <paramref name="report"/>; <paramref name="linux"/> = a Linux computer.</summary>
    public static LocalMusicCard Of(GpuReport report, bool linux)
    {
        ArgumentNullException.ThrowIfNull(report);
        return new LocalMusicCard(report.Adapters.Any(a => a.Vendor == GpuVendor.Nvidia), linux,
            VramTiers.ForCard(report.Largest(GpuVendor.Nvidia)?.VideoMemoryBytes));
    }

    /// <summary>Whether <paramref name="model"/> is a local model made for an (NVIDIA) graphics card: its catalog entry names a card class.</summary>
    public static bool NeedsCard(CatalogModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        return model.Provider == ModelProviders.Local && model.MinVideoMemoryGb > 0;
    }
}
