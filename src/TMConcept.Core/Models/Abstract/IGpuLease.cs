using TMConcept.Core.Hardware;

namespace TMConcept.Core.Models;

/// <summary>
/// The right to use a model for one piece of work (<see cref="IGpuCoordinator.AcquireAsync(IModel, ModelMemoryNeed?, CancellationToken)"/>);
/// dispose it as soon as the work is done (<c>await using</c>). A local model on the graphics card stays loaded after its lease ends, until
/// another local model needs the card.
/// </summary>
public interface IGpuLease : IAsyncDisposable
{
    /// <summary>The model the lease is for.</summary>
    IModel Model { get; }

    /// <summary>Whether it holds the graphics card (a local model on the card); false for an online or a processor model.</summary>
    bool OnCard { get; }

    /// <summary>The card's memory measured when the lease was granted (after the previous model was unloaded); <see cref="VramReading.None"/> off the card.</summary>
    VramReading Memory { get; }
}