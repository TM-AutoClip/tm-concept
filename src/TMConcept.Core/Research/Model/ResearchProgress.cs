namespace TMConcept.Core.Research;

/// <summary>A research run's step: the stage, what it works on and how far it is.</summary>
public sealed record ResearchProgress(ResearchStage Stage, string Name, int Done, int Total);
