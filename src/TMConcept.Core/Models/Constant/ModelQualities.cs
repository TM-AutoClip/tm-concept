namespace TMConcept.Core.Models;

/// <summary>Quality names of <see cref="ModelMemoryNeed.Quality"/>.</summary>
public static class ModelQualities
{
    /// <summary>Smaller weights (more quantized), less memory.</summary>
    public const string Low = "low";

    /// <summary>The usual weights.</summary>
    public const string Standard = "standard";

    /// <summary>The full weights, most memory.</summary>
    public const string High = "high";
}