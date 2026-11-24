namespace TMConcept.Core.Speech;

/// <summary>
/// The plan targets of each <see cref="NarrationTempo"/> (F9, 0.1.42; ours, after the "something changes every 2–3 s" of a lively video and
/// ReelMimic's ease families): tight = about 4 changes in 10 s, a camera move on 6 beats in 10, expo.out / back.out; natural = 3, 4 in 10,
/// power2.inOut (the camera before 0.1.42); relaxed = 1.5, 2 in 10, sine.inOut.
/// </summary>
public static class NarrationTempoTargets
{
    /// <summary>Tight.</summary>
    public static readonly TempoTarget Tight = new(4, 0.6, ["expo.out", "back.out"], Checked: true);

    /// <summary>Natural.</summary>
    public static readonly TempoTarget Natural = new(3, 0.4, ["power2.inOut"], Checked: false);

    /// <summary>Relaxed.</summary>
    public static readonly TempoTarget Relaxed = new(1.5, 0.2, ["sine.inOut"], Checked: true);

    /// <summary>A plan is found below a target when it reaches less than this share of it (tight) …</summary>
    public const double Shortfall = 0.5;

    /// <summary>… and above it when it has more than this many times its changes (relaxed).</summary>
    public const double Excess = 2;
}
