namespace TMConcept.Core.Hardware;

/// <summary>
/// The user's "Image acceleration" setting for the local image models (per computer, in preferences.json). Stored as a number: a new value
/// is added at the end.
/// </summary>
public enum ImageAcceleration
{
    /// <summary>Chosen from the graphics card found (the default): NVIDIA with its driver → CUDA, another card → Vulkan, none → processor.</summary>
    Automatic = 0,

    /// <summary>The NVIDIA CUDA build (Windows; Linux has no CUDA download and uses Vulkan).</summary>
    Cuda = 1,

    /// <summary>The Vulkan build (AMD, Intel and NVIDIA cards; needs only the graphics driver).</summary>
    Vulkan = 2,

    /// <summary>The processor build (no graphics card; slow).</summary>
    Cpu = 3,
}
