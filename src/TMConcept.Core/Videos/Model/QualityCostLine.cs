using TMConcept.Core.Costs;

namespace TMConcept.Core.Videos;

/// <summary>One line of the quality mode's estimate (F11 + F14, 0.1.50): its cost label (<see cref="Pipeline.PipelineSteps"/>) and its range.</summary>
public sealed record QualityCostLine(string Step, CostEstimate Cost);
