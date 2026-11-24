using TMConcept.Core.Speech;

namespace TMConcept.Core.Plans;

/// <summary>
/// The tempo targets of the plan (F9, 0.1.42; <see cref="NarrationTempos.Targets"/>): a tight video needs at least half of its changes (beats
/// with a camera move or an action, about 4 in 10 s, never more than it has sentences) and, from three such beats on, at least half of its
/// camera share; a relaxed one at most twice its changes. A natural video is not checked (every plan before 0.1.42 was made for it), nor
/// a tempo the template's motion contradicts (<see cref="Applies"/>: a calm motion asks for one or two beats per scene and few camera moves,
/// a lively one for a beat on every sentence; the motion wins, otherwise every such video would pay a repair it cannot pass).
/// </summary>
public sealed class TempoTargetRule : PlanRuleBase
{
    /// <summary>
    /// Whether the beat and camera targets of <paramref name="tempo"/> are asked for and checked with <paramref name="motion"/>: not a tight
    /// tempo with a calm motion, not a relaxed one with a lively motion.
    /// </summary>
    public static bool Applies(NarrationTempo tempo, Styles.MotionIntensity motion) =>
        !(tempo == NarrationTempo.Tight && motion == Styles.MotionIntensity.Calm)
        && !(tempo == NarrationTempo.Relaxed && motion == Styles.MotionIntensity.Lively);

    /// <inheritdoc />
    protected override IEnumerable<string> Find(IReadOnlyList<PlanItem> items, PlanCheck check)
    {
        var target = NarrationTempos.Targets(check.Tempo);
        if (!target.Checked || !Applies(check.Tempo, check.Motion))
        {
            yield break;
        }

        var seconds = check.Scenes.Sum(s => s.Seconds);
        var sentences = check.Scenes.Sum(s => Math.Max(1, s.Sentences.Count));
        var beats = Scenes(items, check).SelectMany(p => p.Plan.Beats.Where(b => b.Camera is not null || b.Actions.Count > 0)
            .GroupBy(b => b.Sentence).Select(g => g.First())).ToList();
        var wanted = Math.Min(sentences, Math.Max(1, (int)Math.Round(target.BeatsPer10Seconds * seconds / 10, MidpointRounding.AwayFromZero)));
        if (check.Tempo == NarrationTempo.Tight)
        {
            if (beats.Count < wanted * NarrationTempoTargets.Shortfall)
            {
                yield return string.Create(C,
                    $"the tight tempo wants something new about {target.BeatsPer10Seconds:0.#} times every 10 seconds (about {wanted} beats with a camera move or an action), the plan has {beats.Count}");
            }

            var cameras = beats.Count(b => b.Camera is not null);
            if (beats.Count >= 3 && cameras < beats.Count * target.CameraShare * NarrationTempoTargets.Shortfall)
            {
                yield return string.Create(C,
                    $"the tight tempo wants a camera move on about {target.CameraShare * 10:0} of 10 beats, the plan has {cameras} of {beats.Count}");
            }
        }
        else if (beats.Count > wanted * NarrationTempoTargets.Excess)
        {
            yield return string.Create(C,
                $"the relaxed tempo wants something new only about {target.BeatsPer10Seconds:0.#} times every 10 seconds (about {wanted} beats), the plan has {beats.Count}: leave more sentences without a beat");
        }
    }
}
