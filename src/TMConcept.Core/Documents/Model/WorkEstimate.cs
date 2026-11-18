using TMConcept.Core.Costs;

namespace TMConcept.Core.Documents;

/// <summary>
/// The estimate of a piece of document work before it runs (0.1.24): how many model calls, their tokens and price
/// (<see cref="CostEstimate"/>; zero for a local model) and a rough time range. Always shown as an estimate: real token counts, the
/// provider's speed and this computer's load differ.
/// </summary>
public sealed record WorkEstimate(int Calls, CostEstimate Cost, TimeSpan TimeMin, TimeSpan TimeMax, bool Local)
{
    /// <summary>Nothing to do.</summary>
    public static WorkEstimate None { get; } = new(0, CostEstimate.Zero, TimeSpan.Zero, TimeSpan.Zero, false);

    /// <summary>Sum of two estimates (calls one after the other).</summary>
    public WorkEstimate Plus(WorkEstimate other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return new WorkEstimate(Calls + other.Calls, Cost.Plus(other.Cost), TimeMin + other.TimeMin, TimeMax + other.TimeMax, Local || other.Local);
    }
}
