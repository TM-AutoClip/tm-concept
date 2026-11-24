namespace TMConcept.Core.Videos;

/// <summary>
/// What the motion check knows of the video's plan (<see cref="MotionCheck.Expect"/>): where the scenes start and the closing card begins
/// (a cut there belongs),
/// where a fade through black is planned (it must happen, and its dark frames are not black findings) and where the plan means the picture
/// to stand still (a freeze there is not a finding: a title card, the drawer's scene between its beats, the closing card).
/// </summary>
public sealed record MotionExpectations(IReadOnlyList<double> SceneStarts, IReadOnlyList<double> Fades, IReadOnlyList<(double From, double To)> Still)
{
    /// <summary>Nothing known: every cut, freeze and dark frame is looked at.</summary>
    public static MotionExpectations None { get; } = new([], [], []);
}
