namespace TMConcept.Core.Hardware;

/// <summary>The stable-diffusion.cpp build that runs the local image models on this computer.</summary>
public enum ImageBackend
{
    /// <summary>The processor build.</summary>
    Cpu = 0,

    /// <summary>The Vulkan build (any graphics card with a Vulkan driver).</summary>
    Vulkan = 1,

    /// <summary>The CUDA build (NVIDIA graphics cards, Windows).</summary>
    Cuda = 2,
}
