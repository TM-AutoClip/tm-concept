namespace TMConcept.Core.Models;

/// <summary>
/// Asks the user what to do when a local model's memory is still not free after the readings of <see cref="IMemoryWaiter"/>: close the
/// programs that use the card or the memory and retry, or cancel (the video queue stops); for a model in a process of its own also "Try anyway"
/// (<see cref="MemoryShortfall.CanTryAnyway"/>). The desktop shows a dialog on the interface thread;
/// it closes when the token is canceled (the queue's Cancel) and the call then throws <see cref="OperationCanceledException"/>.
/// </summary>
public interface IMemoryShortfallHandler
{
    /// <summary>Shows the figures and waits for the user's choice.</summary>
    /// <param name="shortfall">What is free and what is needed.</param>
    /// <param name="cancellationToken">Closes the question without a choice.</param>
    Task<MemoryShortfallChoice> AskAsync(MemoryShortfall shortfall, CancellationToken cancellationToken);
}
