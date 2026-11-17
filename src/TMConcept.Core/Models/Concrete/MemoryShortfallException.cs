namespace TMConcept.Core.Models;

/// <summary>
/// A local model's memory was still not free after the readings and nobody was asked (night mode, <see cref="MemoryShortfallScope"/>, or no
/// one to ask): this use of the model fails like any other error (a video's error in night mode). <see cref="Shortfall"/> has the figures.
/// </summary>
public sealed class MemoryShortfallException : InvalidOperationException
{
    /// <summary>Creates.</summary>
    public MemoryShortfallException()
    {
    }

    /// <summary>With a message.</summary>
    public MemoryShortfallException(string message)
        : base(message)
    {
    }

    /// <summary>With a message and an inner error.</summary>
    public MemoryShortfallException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>With the figures (the message names them in English, for the log).</summary>
    public MemoryShortfallException(MemoryShortfall shortfall)
        : base(Describe(shortfall))
    {
        Shortfall = shortfall;
    }

    /// <summary>What was free and what was needed (null when made with a message only).</summary>
    public MemoryShortfall? Shortfall { get; }

    /// <summary>The figures in one English sentence.</summary>
    public static string Describe(MemoryShortfall shortfall)
    {
        ArgumentNullException.ThrowIfNull(shortfall);
        static string Gib(long bytes) => (Math.Max(0, bytes) / (1024.0 * 1024 * 1024)).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
        return $"Not enough memory for {shortfall.ModelName}: {Gib(shortfall.CardFreeBytes)} of the card's {Gib(shortfall.CardTotalBytes)} GiB are free "
            + $"({shortfall.Source}), it needs {Gib(shortfall.NeededVideoBytes)} GiB; main memory: "
            + $"{(shortfall.FreeMainBytes is { } free ? Gib(free) : "?")} GiB free"
            + (shortfall.NeededMainBytes > 0 ? $", it needs {Gib(shortfall.NeededMainBytes)} GiB" : string.Empty) + ".";
    }
}
