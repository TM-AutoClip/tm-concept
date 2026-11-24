using TMConcept.Core.Models;

namespace TMConcept.Speech.Music;

/// <summary>
/// The local music model as a model of the graphics card queue (0.1.46, C5): on the card when there is an NVIDIA one (its needs from the
/// catalog: a 4 GB class and 16 GB of main memory), otherwise on the processor. Each piece is a process of its own that ends with the piece,
/// so there is nothing to load or unload: the lease only keeps other local models off the card while it runs.
/// </summary>
public sealed class MusicLocalModel(CatalogModel catalog, bool nvidia) : LocalModelBase(catalog)
{
    /// <inheritdoc />
    public override ModelDevice Device => nvidia ? ModelDevice.Gpu : ModelDevice.Cpu;
}
