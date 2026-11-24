namespace TMConcept.Core.Plans;

/// <summary>
/// With subtitles on, the bottom quarter is theirs: no box and no point that shows text (its label, or a pin, label or counter at it) lower
/// than <see cref="PlanLimits.SubtitleTop"/>. Map places are fitted by the app into the safe area and are not checked here.
/// </summary>
public sealed class SubtitleAreaRule : PlanRuleBase
{
    /// <inheritdoc />
    protected override IEnumerable<string> Find(IReadOnlyList<PlanItem> items, PlanCheck check)
    {
        if (!check.Subtitles)
        {
            yield break;
        }

        foreach (var (scene, plan) in Scenes(items, check))
        {
            var texted = plan.Beats.SelectMany(b => b.Actions).Where(a => a.Type is PlanActions.Pin or PlanActions.Label or PlanActions.Counter)
                .Select(a => a.At).ToHashSet(StringComparer.Ordinal);
            foreach (var anchor in plan.Anchors.Where(a => a.Y is { } y && y > PlanLimits.SubtitleTop && (a.Label.Length > 0 || texted.Contains(a.Id))))
            {
                yield return string.Create(C,
                    $"scene {scene}: the point {Quote(anchor.Id)} with text is at y {anchor.Y:0.##}, in the bottom quarter kept for the subtitles (y at most {PlanLimits.SubtitleTop:0.##})");
            }

            foreach (var node in plan.Nodes.Where(n => n.Y > PlanLimits.SubtitleTop))
            {
                yield return string.Create(C,
                    $"scene {scene}: the box {Quote(node.Id)} is at y {node.Y:0.##}, in the bottom quarter kept for the subtitles (y at most {PlanLimits.SubtitleTop:0.##})");
            }
        }
    }
}
