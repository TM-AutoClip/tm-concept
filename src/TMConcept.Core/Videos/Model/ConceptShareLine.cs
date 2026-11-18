namespace TMConcept.Core.Videos;

/// <summary>
/// A video's share of the run that generated its concept, with the run's numbers when they are known ("10 concepts cost 0.20 USD → this one
/// 0.02 USD").
/// </summary>
/// <param name="Kind">Where it comes from.</param>
/// <param name="Usd">The share (0 when unknown, hand-written or counted with the first version).</param>
/// <param name="Exact">The run's cost was exact (reported usage), not computed from unit prices.</param>
/// <param name="RunUsd">What the whole run cost; <c>null</c> when the run is not known.</param>
/// <param name="Kept">The concepts the run kept; <c>null</c> when the run is not known.</param>
public sealed record ConceptShareLine(ConceptShareKind Kind, decimal Usd, bool Exact, decimal? RunUsd, int? Kept)
{
    /// <summary>The share is a known amount that belongs to this video (recorded or worked out from the run).</summary>
    public bool Known => Kind is ConceptShareKind.Recorded or ConceptShareKind.FromRun;
}
