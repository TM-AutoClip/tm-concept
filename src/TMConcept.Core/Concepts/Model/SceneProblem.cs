namespace TMConcept.Core.Concepts;

/// <summary>
/// One finding of <see cref="ConceptSceneCheck"/>: <see cref="SceneProblemKind.TooFew"/> with the scenes there are (<paramref name="Have"/>), the
/// fewest accepted (<paramref name="Need"/>) and the target (<paramref name="Target"/>); <see cref="SceneProblemKind.EmptyVisual"/> with the scene's
/// number; <see cref="SceneProblemKind.LongNarration"/> with the scene's number, its estimated seconds (<paramref name="Have"/>) and the most
/// allowed (<paramref name="Need"/>).
/// </summary>
public sealed record SceneProblem(SceneProblemKind Kind, int Scene, int Have, int Need, int Target = 0)
{
    /// <summary>The finding in English, for the log.</summary>
    public override string ToString() => Kind switch
    {
        SceneProblemKind.TooFew => FormattableString.Invariant($"{Have} scenes, at least {Need} needed"),
        SceneProblemKind.EmptyVisual => FormattableString.Invariant($"scene {Scene} has an empty on-screen line"),
        _ => FormattableString.Invariant($"scene {Scene} narration is about {Have} s (at most {Need} s)"),
    };
}
