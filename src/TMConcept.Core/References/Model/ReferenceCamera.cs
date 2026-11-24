namespace TMConcept.Core.References;

/// <summary>The camera of a reference (<see cref="ReferenceCameraMeasure.Measure"/>): the share of shots with a move, its main way and its ease shape.</summary>
public sealed record ReferenceCamera(double Share, CameraDirection Direction, EaseShape Ease);
