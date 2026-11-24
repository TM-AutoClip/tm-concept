namespace TMConcept.Core.Videos;

/// <summary>One missing piece before production (F16, 0.1.49): its code (<see cref="ProductionGaps"/>), its weight and a detail for its text (a provider's name).</summary>
public sealed record ProductionGap(string Code, GapKind Kind, string Detail = "");
