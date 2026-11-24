namespace TMConcept.Core.Plans;

/// <summary>
/// The camera eases a plan may name (F9, 0.1.42; <see cref="PlanCamera.Ease"/>): GSAP's own names, the list ReelMimic's video-clone
/// <c>curves.py</c> (MIT) fits camera moves to. Bounce and elastic are left out (a camera never bounces). An unknown name falls back to the
/// tempo's default (<see cref="Speech.TempoTarget.DefaultEase"/>).
/// </summary>
public static class PlanEases
{
    /// <summary>The ease of a camera move before 0.1.42 (and of a plan that names none).</summary>
    public const string Classic = "power2.inOut";

    /// <summary>Every allowed name.</summary>
    public static readonly IReadOnlyList<string> All =
    [
        "none",
        "power1.in", "power1.out", "power1.inOut",
        "power2.in", "power2.out", "power2.inOut",
        "power3.in", "power3.out", "power3.inOut",
        "power4.in", "power4.out", "power4.inOut",
        "sine.in", "sine.out", "sine.inOut",
        "expo.in", "expo.out", "expo.inOut",
        "circ.in", "circ.out",
        "back.in", "back.out",
    ];
}
