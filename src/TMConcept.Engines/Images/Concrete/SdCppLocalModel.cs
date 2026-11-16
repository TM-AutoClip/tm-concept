using TMConcept.Core.Hardware;
using TMConcept.Core.Models;

namespace TMConcept.Engines.Images;

/// <summary>
/// A local image model run by stable-diffusion.cpp: on the graphics card unless the processor build is chosen (<see cref="ImageBackendSelector"/>,
/// read at every lease). Every image is one sd-cli process that ends with the image, so there is nothing to keep loaded or to unload. Its
/// graphics memory need is the sum of its weight files (the build moves what does not fit to main memory, more slowly), at least the
/// catalog's class; main memory needs the same once more when the weights move off the card.
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
            var weights = files.Files.Sum(f => f.ApproximateBytes);
            var video = Device == ModelDevice.Gpu ? Math.Max(weights, Catalog.MinVideoMemoryGb * Gibibyte) : 0;
            return [new ModelMemoryNeed(ModelQualities.Standard, video, Math.Max(weights, Catalog.MemoryGb * Gibibyte))];
        }
    }
}