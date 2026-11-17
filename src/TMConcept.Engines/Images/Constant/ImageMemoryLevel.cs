namespace TMConcept.Engines.Images;

/// <summary>
/// How an image is made with the memory there is (<see cref="ImageMemoryPlanner"/>), best first: card, then main memory, then the disk
/// (0.1.18, the developer: the model never changes, only where its weights live). A run that ends out of memory steps down one level at a time.
/// </summary>
public enum ImageMemoryLevel
{
    /// <summary>The diffusion model fits on the card: sd-cli places the weights itself (tiled VAE, a reserve kept free).</summary>
    Card = 1,

    /// <summary>
    /// It does not fit, but main memory holds what is left over: sd-cli's own placement (auto-fit: card first, then main memory) with the
    /// weights memory-mapped (<c>--mmap</c>). As 0.1.16 ran Z-Image Q8 on the 6 GB laptop. Slower.
    /// </summary>
    MainMemory = 2,

    /// <summary>
    /// Main memory is short too: the diffusion weights are read from the disk when needed, the text encoder and the VAE stay in main memory
    /// (<c>--params-backend diffusion=disk,te=cpu,vae=cpu</c>). The slowest level that still runs on the card.
    /// </summary>
    Disk = 3,

    /// <summary>As <see cref="Disk"/>, and the text encoder also runs on the processor (<c>--backend te=cpu</c>): the least graphics memory.</summary>
    DiskTextEncoder = 4,

    /// <summary>The processor build: no graphics card at all.</summary>
    Processor = 5,

    /// <summary>Not even the disk level can run: the card lacks the working memory; not started, with the reason.</summary>
    Refused = 6,
}
