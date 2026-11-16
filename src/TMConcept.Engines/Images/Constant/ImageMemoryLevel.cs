namespace TMConcept.Engines.Images;

/// <summary>
/// How an image is made with the memory there is (<see cref="ImageMemoryPlanner"/>), best first. A run that ends out of memory steps down
/// one level and is tried once more.
/// </summary>
public enum ImageMemoryLevel
{
    /// <summary>The diffusion model fits on the card: sd-cli places the weights itself (tiled VAE, a reserve kept free).</summary>
    Card = 1,

    /// <summary>The weights stay in main memory and go to the card piece by piece (<c>--offload-to-cpu</c>).</summary>
    Offload = 2,

    /// <summary>As <see cref="Offload"/>, and the text encoder also runs on the processor (<c>--backend te=cpu</c>): the least graphics memory.</summary>
    OffloadTextEncoder = 3,

    /// <summary>The processor build: no graphics card at all.</summary>
    Processor = 4,

    /// <summary>Neither the card nor main memory has room: not started, with the reason.</summary>
    Refused = 5,
}
