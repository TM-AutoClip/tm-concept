using TMConcept.Core.Hardware;
using TMConcept.Core.Models;

namespace TMConcept.Engines.Images;

/// <summary>
/// A local image model run by stable-diffusion.cpp: on the graphics card unless the processor build is chosen (<see cref="ImageBackendSelector"/>,
/// read at every lease). Every image is one sd-cli process that ends with the image, so there is nothing to keep loaded or to unload. Its
/// graphics memory need is what a card of its class has free (7/8 of the class: Windows keeps about an eighth); main memory needs the
/// weights' size.
/// <para>Its memory behaviour (0.1.18, the developer: every local model's own class says what it does on which card):
/// <see cref="MemorySettings"/> gives the figures for a card (the default table here, by card class), <see cref="PlanImage"/> turns them into
/// sd-cli's options with <see cref="ImageMemoryPlanner"/> (card → main memory → disk, never another model), and <see cref="PlanMemory"/> is the
/// same plan for <see cref="IGpuCoordinator"/>'s check. A family that behaves differently overrides <see cref="MemorySettings"/>
/// (<see cref="ZImageLocalModel"/>).</para>
/// </summary>
public abstract class SdCppModelBase(CatalogModel catalog, LocalImageModel files, ImageBackendSelector backends) : LocalModelBase(catalog)
{
    private const long Gibibyte = 1024L * 1024 * 1024;

    /// <summary>The figures on a 4 GB card: less working memory on the card, 1.25 GiB off it.</summary>
    private static readonly ImageMemorySettings Small = new(2 * Gibibyte, 1280L * 1024 * 1024, Gibibyte);

    /// <summary>The model's files and sampling.</summary>
    public LocalImageModel Files => files;

    /// <inheritdoc />
    public override ModelDevice Device => backends.Choose().Backend == ImageBackend.Cpu ? ModelDevice.Cpu : ModelDevice.Gpu;

    /// <inheritdoc />
    public override IReadOnlyList<ModelMemoryNeed> Needs
    {
        get
        {
            var video = Device == ModelDevice.Gpu ? VramTiers.Gigabytes(files.Tier) * Gibibyte / 8 * 7 : 0;
            return [new ModelMemoryNeed(ModelQualities.Standard, video, Math.Max(files.TotalBytes, Catalog.MemoryGb * Gibibyte))];
        }
    }

    /// <summary>
    /// This model's memory figures on <paramref name="card"/> (its class from the total memory, <see cref="VramTiers.ForCard"/>). The default
    /// table: a 4 GB card (or an unknown one) 2 GiB working memory on the card and 1.25 GiB off it; every larger card
    /// <see cref="ImageMemoryPlanner.DefaultSettings"/> (2.5 GiB on the card, 1.5 GiB off it). 1 GiB of main memory is kept free.
    /// </summary>
    public virtual ImageMemorySettings MemorySettings(VramReading card)
    {
        ArgumentNullException.ThrowIfNull(card);
        return (int)card.Tier <= (int)VramTier.Gb4 ? Small : ImageMemoryPlanner.DefaultSettings;
    }

    /// <summary>The plan of one picture with the <paramref name="backend"/> build and the memory of <paramref name="memory"/>.</summary>
    public ImageMemoryPlan PlanImage(ImageBackend backend, MemorySnapshot memory)
    {
        ArgumentNullException.ThrowIfNull(memory);
        return ImageMemoryPlanner.Plan(files, backend, memory.Card, memory.FreeMainBytes, MemorySettings(memory.Card));
    }

    /// <inheritdoc />
    public override MemoryPlan PlanMemory(MemorySnapshot memory, ModelMemoryNeed? need) => PlanImage(backends.Choose().Backend, memory).ToMemoryPlan();
}
