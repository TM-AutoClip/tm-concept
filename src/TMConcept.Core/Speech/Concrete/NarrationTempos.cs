namespace TMConcept.Core.Speech;

/// <summary>
/// The silences of each <see cref="NarrationTempo"/>. A clip keeps <see cref="NarrationGaps.TailKeepMs"/> after its last word and
/// <see cref="NarrationGaps.LeadKeepMs"/> before its first, so what a listener hears is about 260 ms more than the numbers here: tight
/// ≈ 0.36 s between sentences and 0.56 s between scenes, natural ≈ 0.56 s and 0.91 s, relaxed ≈ 0.81 s and 1.31 s.
/// </summary>
public static class NarrationTempos
{
    /// <summary>The tempo of a template that has none (and of a video made without a template).</summary>
    public const NarrationTempo Default = NarrationTempo.Natural;

    /// <summary>The added silences of <paramref name="tempo"/> (an unknown value reads as <see cref="Default"/>).</summary>
    public static NarrationPacing Of(NarrationTempo tempo) => tempo switch
    {
        NarrationTempo.Tight => new NarrationPacing(100, 300),
        NarrationTempo.Relaxed => new NarrationPacing(550, 1050),
        _ => new NarrationPacing(300, 650),
    };
}
