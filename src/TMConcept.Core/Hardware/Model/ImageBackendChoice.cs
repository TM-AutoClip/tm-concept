namespace TMConcept.Core.Hardware;

/// <summary>The stable-diffusion.cpp build the local image models use now, and why.</summary>
/// <param name="Backend">The build.</param>
/// <param name="Setting">The user's setting it came from.</param>
/// <param name="Device">The graphics card it runs on (its memory sets the memory flags); null on the processor.</param>
public sealed record ImageBackendChoice(ImageBackend Backend, ImageAcceleration Setting, GpuAdapter? Device);
