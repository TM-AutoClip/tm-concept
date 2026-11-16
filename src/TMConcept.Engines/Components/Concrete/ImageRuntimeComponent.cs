using TMConcept.Core.Components;
using TMConcept.Core.Hardware;
using TMConcept.Core.Tools;
using TMConcept.Engines.Images;
using TMConcept.Engines.Tools;

namespace TMConcept.Engines.Components;

/// <summary>
/// stable-diffusion.cpp: exactly the build the <see cref="ImageBackendSelector"/> chooses now (CUDA with its CUDA runtime libraries, Vulkan
/// or processor). Changing the Image acceleration setting makes it not ready until that build is installed; the other builds' folders stay
/// on the list (removed together with it).
/// </summary>
public sealed class ImageRuntimeComponent(ToolInstaller installer, ImageBackendSelector backends)
    : ToolComponentBase(installer, ComponentCodes.ImageRuntime, "stable-diffusion.cpp", [], [])
{
    /// <inheritdoc />
    public override string Name => "stable-diffusion.cpp (" + Label(backends.Choose().Backend) + ")";

    /// <inheritdoc />
    protected override IReadOnlyList<ToolKind> Tools => ImageBackendSelector.Tools(backends.Choose().Backend);

    /// <inheritdoc />
    protected override IReadOnlyList<ToolKind> FolderTools => ImageBackendSelector.AllTools;

    private static string Label(ImageBackend backend) => backend switch
    {
        ImageBackend.Cuda => "NVIDIA CUDA",
        ImageBackend.Vulkan => "Vulkan",
        _ => "CPU",
    };
}
