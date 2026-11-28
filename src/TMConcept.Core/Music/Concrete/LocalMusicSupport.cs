using TMConcept.Core.Hardware;
using TMConcept.Core.Models;

namespace TMConcept.Core.Music;

/// <summary>
/// Whether a local music model fits this computer (0.1.46, C5; the developer's rule of 23 Nov 2026: "Müzik modeli yalnız NVIDIA"). Pure:
/// the card report comes from <see cref="IGpuInfo"/>, the system from the caller. Only a model that needs a graphics card
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

    /// <summary>
    /// Whether <paramref name="model"/> needs a higher graphics card class than this computer's NVIDIA card (28 Nov 2026: red "this card cannot
    /// run it", the same rule as the image models and the voices; never a block, rule 19). False without an NVIDIA card or when its memory is
    /// unknown (the "NVIDIA only" note says that already) and for a model that needs no card.
    /// </summary>
    public static bool TooBig(CatalogModel model, LocalMusicCard card)
    {
        ArgumentNullException.ThrowIfNull(card);
        return NeedsCard(model) && card.Nvidia && card.Tier != VramTier.None && !VramTiers.Sees(card.Tier, VramTiers.ForModel(model.MinVideoMemoryGb));
    }

    /// <summary>Whether <paramref name="model"/> is made for this computer: an online model or one that runs on the processor always, a card model with NVIDIA.</summary>
    public static bool Fits(CatalogModel model, LocalMusicCard card)
    {
        ArgumentNullException.ThrowIfNull(card);
        return !NeedsCard(model) || card.Nvidia;
    }

    /// <summary>
    /// The music models in the order the pages offer them: the local ones first, of them the ones made for this computer first (so the first
    /// choice of a page with nothing installed is a model that runs here: without NVIDIA "Program composes", not a 23 GB card model), then the
    /// ones this card's class holds before the ones it does not (28 Nov 2026), the catalog's order otherwise.
    /// </summary>
    public static IReadOnlyList<CatalogModel> Ordered(IEnumerable<CatalogModel> models, LocalMusicCard card)
    {
        ArgumentNullException.ThrowIfNull(models);
        ArgumentNullException.ThrowIfNull(card);
        return models.OrderBy(m => m.Provider != ModelProviders.Local).ThenBy(m => !Fits(m, card)).ThenBy(m => TooBig(m, card)).ToList();
    }

    /// <summary>Whether <paramref name="model"/> is a local model made for an (NVIDIA) graphics card: its catalog entry names a card class.</summary>
    public static bool NeedsCard(CatalogModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        return model.Provider == ModelProviders.Local && model.MinVideoMemoryGb > 0;
    }
}
