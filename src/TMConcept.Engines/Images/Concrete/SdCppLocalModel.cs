using TMConcept.Core.Hardware;
using TMConcept.Core.Models;

namespace TMConcept.Engines.Images;

/// <summary>
/// A local image model run by stable-diffusion.cpp: on the graphics card unless the processor build is chosen (<see cref="ImageBackendSelector"/>,
/// read at every lease). Every image is one sd-cli process that ends with the image, so there is nothing to keep loaded or to unload. Its
/// graphics memory need is what a card of its class has free (7/8 of the class: Windows keeps about an eighth); sd-cli keeps what does not
/// fit in main memory (<see cref="ImageMemoryPlanner"/>), so main memory needs the weights' size.
/// </summary>
public sealed class SdCppLocalModel(CatalogModel catalog, LocalImageModel files, ImageBackendSelector backends) : LocalModelBase(catalog)
{
    private const long Gibibyte = 1024L * 1024 * 1024;

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
}
