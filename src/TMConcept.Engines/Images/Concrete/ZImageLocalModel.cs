using TMConcept.Core.Hardware;
using TMConcept.Core.Models;

namespace TMConcept.Engines.Images;

/// <summary>
/// A Z-Image Turbo model (<c>z-image</c> family). On cards up to the 6 GB class its diffusion model (Q8: 6.1 GiB) never fits on the card, so
/// it always runs from main memory or the disk there; it then needs only 1.25 GiB of working memory on the card (0.1.16 made Z-Image Turbo Q8
/// pictures on the 6 GB RTX 3060 Laptop with <c>--max-vram 4</c> and sd.cpp's own placement, about 71 s each). Larger cards use the defaults.
/// </summary>
public sealed class ZImageLocalModel(CatalogModel catalog, LocalImageModel files, ImageBackendSelector backends) : SdCppModelBase(catalog, files, backends)
{
    /// <summary>The figures on cards up to the 6 GB class: 1.25 GiB of working memory off the card.</summary>
    public static readonly ImageMemorySettings UpTo6Gb = new(ImageMemoryPlanner.WorkingBytes, 1280L * 1024 * 1024, ImageMemoryPlanner.Gibibyte);

    /// <inheritdoc />
    public override ImageMemorySettings MemorySettings(VramReading card)
    {
        ArgumentNullException.ThrowIfNull(card);
        return card.Tier is VramTier.Gb6 ? UpTo6Gb : base.MemorySettings(card);
    }
}
