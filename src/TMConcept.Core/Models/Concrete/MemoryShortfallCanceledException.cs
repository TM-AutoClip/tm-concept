namespace TMConcept.Core.Models;

/// <summary>
/// The user chose Cancel in the memory question (<see cref="IMemoryShortfallHandler"/>): a cancellation, so every step lets it through; the
/// video queue stops on it (the app stays open). <see cref="Shortfall"/> has the figures.
/// </summary>
public sealed class MemoryShortfallCanceledException : OperationCanceledException
{
    /// <summary>Creates.</summary>
    public MemoryShortfallCanceledException()
    {
    }

    /// <summary>With a message.</summary>
    public MemoryShortfallCanceledException(string message)
        : base(message)
    {
    }

    /// <summary>With a message and an inner error.</summary>
    public MemoryShortfallCanceledException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>With the figures.</summary>
    public MemoryShortfallCanceledException(MemoryShortfall shortfall)
        : base("Canceled by the user: " + MemoryShortfallException.Describe(shortfall))
    {
        Shortfall = shortfall;
    }

    /// <summary>What was free and what was needed (null when made with a message only).</summary>
    public MemoryShortfall? Shortfall { get; }
}
