namespace TMConcept.Engines.Images;

/// <summary>
/// A local image model's memory figures for one card (<see cref="SdCppModelBase.MemorySettings"/>, per card class and measured memory; the
/// planner applies them in <see cref="ImageMemoryPlanner"/>).
/// </summary>
/// <param name="CardWorkingBytes">Working memory besides the weights when the diffusion model is on the card (sd.cpp's compute buffers and the tiled VAE).</param>
/// <param name="MinimumWorkingBytes">
/// The least free card memory (after the reserve) for the main memory and disk levels: sd.cpp splits the work under <c>--max-vram</c> and keeps
/// its own scratch, so it is less than <see cref="CardWorkingBytes"/>. Below it nothing can run: the only refusal.
/// </param>
/// <param name="MainMemoryHeadroomBytes">Main memory kept free besides the weights that wait there (the system and other programs).</param>
public sealed record ImageMemorySettings(long CardWorkingBytes, long MinimumWorkingBytes, long MainMemoryHeadroomBytes);
