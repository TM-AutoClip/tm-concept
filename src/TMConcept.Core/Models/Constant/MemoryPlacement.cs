namespace TMConcept.Core.Models;

/// <summary>
/// Where a local model's weights go with the memory there is now (<see cref="ILocalModel.PlanMemory"/>), best first. The model itself never
/// changes (0.1.18, the developer: never switch to another model silently); only where its weights live does. Anything but
/// <see cref="Card"/> and <see cref="Processor"/> is slower and is shown to the user as a yellow line.
/// </summary>
public enum MemoryPlacement
{
    /// <summary>Everything fits on the graphics card.</summary>
    Card = 1,

    /// <summary>The weights that do not fit on the card stay in main memory (RAM) and go to the card when needed: slower.</summary>
    MainMemory = 2,

    /// <summary>Main memory is short too: the largest weights are read from the disk when needed: the slowest level that still runs.</summary>
    Disk = 3,

    /// <summary>The model runs on the processor: no graphics card is involved.</summary>
    Processor = 4,

    /// <summary>Not even the disk level can run: the card lacks the working memory the model needs besides its weights.</summary>
    Refused = 5,
}
