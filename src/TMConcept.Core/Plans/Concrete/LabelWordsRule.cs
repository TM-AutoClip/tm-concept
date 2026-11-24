namespace TMConcept.Core.Plans;

/// <summary>
/// A label has at most <see cref="PlanLimits.LabelWords"/> words: the labels of the points and the boxes, an action's label and the words of
/// a label action. A text action is a statement card, not a label.
/// </summary>
public sealed class LabelWordsRule : PlanRuleBase
{
    /// <inheritdoc />
    protected override IEnumerable<string> Find(IReadOnlyList<PlanItem> items, PlanCheck check)
    {
        foreach (var (scene, plan) in Scenes(items, check))
        {
            var labels = plan.Anchors.Select(a => a.Label).Concat(plan.Nodes.Select(n => n.Label))
                .Concat(plan.Beats.SelectMany(b => b.Actions).SelectMany(a => a.Type == PlanActions.Label ? new[] { a.Label, a.Text } : [a.Label]));
            foreach (var label in labels.Where(l => Words(l) > PlanLimits.LabelWords).Distinct(StringComparer.Ordinal))
            {
                yield return string.Create(C, $"scene {scene}: the label {Quote(label)} has {Words(label)} words (at most {PlanLimits.LabelWords})");
            }
        }
    }

    /// <summary>The words of a label (split at white space).</summary>
    public static int Words(string? text) => (text ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
}
