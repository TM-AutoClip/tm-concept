namespace TMConcept.Core.Plans;

/// <summary>
/// Every action points at points of its own scene (cleaning drops an action whose point is missing, and the moment it should show is lost):
/// a path's or an arrow's ends and stops, a pin's, label's or highlight's point, a counter's point when it has one.
/// </summary>
public sealed class ActionPointsRule : PlanRuleBase
{
    /// <inheritdoc />
    protected override IEnumerable<string> Find(IReadOnlyList<PlanItem> items, PlanCheck check)
    {
        foreach (var (scene, plan) in Scenes(items, check))
        {
            var points = Points(plan);
            foreach (var action in plan.Beats.SelectMany(b => b.Actions))
            {
                IEnumerable<string> used = action.Type switch
                {
                    PlanActions.Path or PlanActions.Arrow => [action.From, action.To, .. action.Via],
                    PlanActions.Pin or PlanActions.Label or PlanActions.Highlight => [action.At],
                    PlanActions.Counter when action.At.Length > 0 => [action.At],
                    _ => [],
                };
                foreach (var id in used.Where(id => !points.Contains(id)).Distinct(StringComparer.Ordinal))
                {
                    yield return id.Length == 0
                        ? string.Create(C, $"scene {scene}: the {action.Type} action has no point")
                        : string.Create(C, $"scene {scene}: the {action.Type} action points at {Quote(id)}, which is not a point of that scene");
                }
            }
        }
    }
}
