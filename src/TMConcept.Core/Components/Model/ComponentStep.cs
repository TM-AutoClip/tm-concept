namespace TMConcept.Core.Components;

/// <summary>
/// One component of an install plan as it changes: its place in the plan (<see cref="Index"/>), its state, its own share 0–1 and the
/// component's progress text (what is downloaded or unpacked, the error when it failed).
/// </summary>
public sealed record ComponentStep(int Index, string Code, ComponentStepState State, double Share, string Detail);
