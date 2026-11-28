namespace TMConcept.Core.Speech;

/// <summary>
/// The silences of each <see cref="NarrationTempo"/>. The numbers here are what a listener HEARS between two sentences and between two scenes
/// (0.1.62): tight 0.15 s / 0.2 s, natural 0.25 s / 0.3 s, relaxed 0.4 s / 0.5 s. A clip already keeps <see cref="NarrationGaps.TailKeepMs"/>
/// after its last word and <see cref="NarrationGaps.LeadKeepMs"/> before its first, so the silence the program adds is the heard total less
/// those margins (<see cref="Of"/>). Until 0.1.61 the added silence alone was 300 / 650 ms (natural), heard as about 0.56 s and 0.91 s; the
/// developer: "1-second breaths are not needed ... 0.25 s is enough". A scene change also has its picture transition on top; it is not waited for.
/// </summary>
public static class NarrationTempos
{
    /// <summary>The tempo of a template that has none (and of a video made without a template).</summary>
    public const NarrationTempo Default = NarrationTempo.Natural;

    /// <summary>The silence a listener hears between two sentences and between two scenes of <paramref name="tempo"/> (an unknown value reads as <see cref="Default"/>).</summary>
    public static NarrationPacing Heard(NarrationTempo tempo) => tempo switch
    {
        NarrationTempo.Tight => new NarrationPacing(150, 200),
        NarrationTempo.Relaxed => new NarrationPacing(400, 500),
        _ => new NarrationPacing(250, 300),
    };

    /// <summary>
    /// The silences the program adds for <paramref name="tempo"/>: <see cref="Heard"/> less the margins every trimmed clip keeps (never below
    /// zero; an unknown value reads as <see cref="Default"/>).
    /// </summary>
    public static NarrationPacing Of(NarrationTempo tempo)
    {
        var heard = Heard(tempo);
        const int margins = NarrationGaps.LeadKeepMs + NarrationGaps.TailKeepMs;
        return new NarrationPacing(Math.Max(0, heard.SentenceGapMs - margins), Math.Max(0, heard.SceneGapMs - margins));
    }

    /// <summary>The plan targets of <paramref name="tempo"/> (F9, 0.1.42; an unknown value reads as <see cref="Default"/>).</summary>
    public static TempoTarget Targets(NarrationTempo tempo) => tempo switch
    {
        NarrationTempo.Tight => NarrationTempoTargets.Tight,
        NarrationTempo.Relaxed => NarrationTempoTargets.Relaxed,
        _ => NarrationTempoTargets.Natural,
    };
}
