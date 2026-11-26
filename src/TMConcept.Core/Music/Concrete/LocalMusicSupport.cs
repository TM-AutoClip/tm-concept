using TMConcept.Core.Hardware;
using TMConcept.Core.Models;

namespace TMConcept.Core.Music;

/// <summary>
/// Whether a local music model fits this computer (0.1.46, C5; the developer's rule of 23 Nov 2026: "Müzik modeli yalnız NVIDIA"). Pure:
/// the card report comes from <see cref="IGpuInfo"/>, the system from the caller. Only a model that needs a graphics card
/// (<see cref="NeedsCard"/>: its catalog entry names a card class, ACE-Step 1.5) carries the card note; a model that runs on the processor does not.
/// </summary>
public static class LocalMusicSupport
{
    /// <summary>The local music models on a computer with <paramref name="report"/>; <paramref name="linux"/> = a Linux computer.</summary>
    public static LocalMusicCard Of(GpuReport report, bool linux)
    {
        ArgumentNullException.ThrowIfNull(report);
        return new LocalMusicCard(report.Adapters.Any(a => a.Vendor == GpuVendor.Nvidia), linux);
    }

    /// <summary>Whether <paramref name="model"/> is made for this computer: an online model or one that runs on the processor always, a card model with NVIDIA.</summary>
    public static bool Fits(CatalogModel model, LocalMusicCard card)
    {
        ArgumentNullException.ThrowIfNull(card);
        return !NeedsCard(model) || card.Nvidia;
    }

    /// <summary>
    /// The music models in the order the pages offer them: the local ones first, of them the ones made for this computer first (so the first
    /// choice of a page with nothing installed is a model that runs here: without NVIDIA "Program composes", not a 23 GB card model), the
    /// catalog's order otherwise.
    /// </summary>
    public static IReadOnlyList<CatalogModel> Ordered(IEnumerable<CatalogModel> models, LocalMusicCard card)
    {
        ArgumentNullException.ThrowIfNull(models);
        ArgumentNullException.ThrowIfNull(card);
        return models.OrderBy(m => m.Provider != ModelProviders.Local).ThenBy(m => !Fits(m, card)).ToList();
    }

    /// <summary>Whether <paramref name="model"/> is a local model made for an (NVIDIA) graphics card: its catalog entry names a card class.</summary>
    public static bool NeedsCard(CatalogModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        return model.Provider == ModelProviders.Local && model.MinVideoMemoryGb > 0;
    }
}
