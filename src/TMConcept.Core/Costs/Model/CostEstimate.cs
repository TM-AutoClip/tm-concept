namespace TMConcept.Core.Costs;

/// <summary>An estimated cost range with the token counts it was computed from.</summary>
public sealed record CostEstimate(decimal MinUsd, decimal MaxUsd, long InputTokens, long OutputTokensMin, long OutputTokensMax)
{
    /// <summary>Nothing to pay.</summary>
    public static CostEstimate Zero { get; } = new(0, 0, 0, 0, 0);

    /// <summary>Sum of two estimates.</summary>
    public CostEstimate Plus(CostEstimate other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return new CostEstimate(MinUsd + other.MinUsd, MaxUsd + other.MaxUsd, InputTokens + other.InputTokens, OutputTokensMin + other.OutputTokensMin,
            OutputTokensMax + other.OutputTokensMax);
    }
}
