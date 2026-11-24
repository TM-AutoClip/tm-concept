namespace TMConcept.Core.Plans;

/// <summary>
/// A sentence has at most <see cref="PlanLimits.BeatsPerSentence"/> beats, and a beat belongs to a sentence the scene has (cleaning would
/// merge the extra beats and move a stray one to the last sentence; the model is told instead).
/// </summary>
public sealed class BeatsPerSentenceRule : PlanRuleBase
{
    /// <inheritdoc />
    protected override IEnumerable<string> Find(IReadOnlyList<PlanItem> items, PlanCheck check)
    {
        foreach (var (scene, plan) in Scenes(items, check))
        {
            var sentences = Math.Max(1, check.Scenes[scene - 1].Sentences.Count);
            foreach (var group in plan.Beats.GroupBy(b => b.Sentence).OrderBy(g => g.Key))
            {
                if (group.Key < 1 || group.Key > sentences)
                {
                    yield return string.Create(C, $"scene {scene} has a beat for sentence {group.Key} but only {sentences} sentences");
                }
                else if (group.Count() > PlanLimits.BeatsPerSentence)
                {
                    yield return string.Create(C,
                        $"scene {scene} has {group.Count()} beats for sentence {group.Key} (at most {PlanLimits.BeatsPerSentence})");
                }
            }
        }
    }
}
