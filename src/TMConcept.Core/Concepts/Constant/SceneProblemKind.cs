namespace TMConcept.Core.Concepts;

/// <summary>What is wrong with the scenes of a concept (<see cref="ConceptSceneCheck"/>).</summary>
public enum SceneProblemKind
{
    /// <summary>Fewer scenes than the target length needs.</summary>
    TooFew = 0,

    /// <summary>A scene says nothing about what is on screen.</summary>
    EmptyVisual = 1,

    /// <summary>A scene's narration runs too long for one picture.</summary>
    LongNarration = 2,
}
