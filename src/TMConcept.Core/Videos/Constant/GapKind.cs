namespace TMConcept.Core.Videos;

/// <summary>How a missing piece before production weighs (F16, 0.1.49; <see cref="ProductionChecklist"/>).</summary>
public enum GapKind
{
    /// <summary>Nothing can be produced until it is fixed ("Produce" is off).</summary>
    Required = 0,

    /// <summary>Fixed, or skipped on purpose (the video is then made without it); it never turns "Produce" off.</summary>
    Optional = 1,
}
