namespace TMConcept.Core.Videos;

/// <summary>
/// What the motion check knows of the video's plan (<see cref="MotionCheck.Expect"/>): where the scenes start and the closing card begins
/// (a cut there belongs),
/// where a fade through black is planned (it must happen, and its dark frames are not black findings) and where the plan means the picture
/// to stand still (a freeze there is not a finding: a title card, the drawer's scene between its beats, the closing card) and (0.1.42) where
/// the drawer moves from one scene to the next (<paramref name="Transitions"/>: a slide, a push, a wipe or a zoom-through changes most of the
/// frame at once, which is no cut finding there; null = none).
/// </summary>
public sealed record MotionExpectations(IReadOnlyList<double> SceneStarts, IReadOnlyList<double> Fades, IReadOnlyList<(double From, double To)> Still,
    IReadOnlyList<(double From, double To)>? Transitions = null)
{
    /// <summary>Nothing known: every cut, freeze and dark frame is looked at.</summary>
    public static MotionExpectations None { get; } = new([], [], []);
}
