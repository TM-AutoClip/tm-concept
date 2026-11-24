namespace TMConcept.Core.References;

/// <summary>Where a reference measure is: its stage (<see cref="ReferenceStages"/>) and how far that stage is (0–1).</summary>
public sealed record ReferenceProgress(string Stage, double Fraction);
