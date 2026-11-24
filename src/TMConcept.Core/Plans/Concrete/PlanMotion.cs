using TMConcept.Core.Scenes;
using TMConcept.Core.Speech;

namespace TMConcept.Core.Plans;

/// <summary>
/// Settles the motion of the cleaned plans (F1, F9, S4; 0.1.42), with no model call: every scene after the first gets a transition of the
/// catalog that fits the tempo's energy (the plan's choice when it fits, otherwise the drawing style's family or the energy's default; a
/// style with a family uses only that family), every directional transition goes the film's one direction (the way a scene leaves is the way
/// the next one enters), a scene whose text asks for its own transition (a placeholder) and the first scene get none; every camera move gets
/// an allowed ease (an unknown or missing one falls back to the tempo's default); and for a tight tempo a still picture's slow zoom becomes a
/// move in the film's direction (natural and relaxed keep the zoom they had).
/// </summary>
public static class PlanMotion
{
    /// <summary>
    /// Applies the rules to <paramref name="plans"/> (in scene order). <paramref name="asked"/>: each scene's placeholder transition
    /// (<see cref="SceneTransitions.Cut"/>, <see cref="SceneTransitions.Fade"/> or empty); <paramref name="styleFamily"/>: the drawing style's
    /// transition family (<see cref="Styles.DrawStyleDefinition.Transition"/>, empty = none).
    /// </summary>
    public static void Apply(IReadOnlyList<PlanItem> plans, IReadOnlyList<string> asked, NarrationTempo tempo, string styleFamily) =>
        Apply(plans, asked, tempo, TransitionCatalog.One(styleFamily));

    /// <summary>
    /// The same for a style with several families (F13, 0.1.51, <see cref="Styles.DrawStyleDefinition.Families"/>): the plan's choice is kept
    /// when it is one of them, otherwise the first that fits the energy. A family only a style may use (<see cref="TransitionDefinition.StyleOnly"/>)
    /// is never kept for a video whose style does not name it. <paramref name="target"/> (S1, 0.1.52): a reference video's targets, whose
    /// first ease is the default of a camera move (null = the tempo's).
    /// </summary>
    public static void Apply(IReadOnlyList<PlanItem> plans, IReadOnlyList<string> asked, NarrationTempo tempo, IReadOnlyList<string> styleFamilies,
        TempoTarget? target = null)
    {
        ArgumentNullException.ThrowIfNull(plans);
        ArgumentNullException.ThrowIfNull(asked);
        ArgumentNullException.ThrowIfNull(styleFamilies);
        var energy = TransitionCatalog.EnergyOf(tempo);
        var families = TransitionCatalog.StyleFamilies(styleFamilies, energy).Select(d => d.Family).ToList();
        var direction = Direction(plans.Skip(1).Select(p => p.Plan.Transition));
        for (var i = 0; i < plans.Count; i++)
        {
            var plan = plans[i].Plan;
            if (i == 0 || (i < asked.Count && asked[i].Length > 0))
            {
                plan.Transition = string.Empty;
                plan.TransitionSeconds = 0;
                continue;
            }

            var found = TransitionCatalog.Allowed(plan.Transition, energy) ? TransitionCatalog.Find(plan.Transition)!.Value.Definition : null;
            var chosen = found?.Family ?? string.Empty;
            if (chosen.Length == 0 || (families.Count > 0 ? !families.Contains(chosen) : found!.StyleOnly))
            {
                chosen = TransitionCatalog.DefaultFamily(energy, styleFamilies);
            }

            plan.Transition = TransitionCatalog.Name(chosen, direction);
            plan.TransitionSeconds = TransitionCatalog.Seconds(chosen, energy);
        }

        target ??= NarrationTempos.Targets(tempo);
        var directional = plans.Any(p => TransitionCatalog.Find(p.Plan.Transition) is { Definition.Directional: true });
        foreach (var plan in plans.Select(p => p.Plan))
        {
            foreach (var camera in plan.Beats.Select(b => b.Camera).OfType<PlanCamera>())
            {
                camera.Ease = Ease(camera.Ease, target);
            }

            plan.Drift = tempo == NarrationTempo.Tight ? (directional ? direction : PlanDrifts.In) : PlanDrifts.Zoom;
        }
    }

    /// <summary>An allowed ease as GSAP spells it, or the tempo's default for an unknown or missing one.</summary>
    public static string Ease(string? ease, TempoTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        var name = (ease ?? string.Empty).Trim();
        return PlanEases.All.FirstOrDefault(e => string.Equals(e, name, StringComparison.OrdinalIgnoreCase)) ?? target.DefaultEase;
    }

    /// <summary>
    /// The film's one direction: the one the plan's directional transitions name most (the first named on a tie), or
    /// <see cref="SceneTransitions.Left"/> when they name none.
    /// </summary>
    public static string Direction(IEnumerable<string> transitions)
    {
        ArgumentNullException.ThrowIfNull(transitions);
        var named = transitions.Select(TransitionCatalog.Find).Where(f => f is { Definition.Directional: true }).Select(f => f!.Value.Direction).ToList();
        return named.Count == 0
            ? SceneTransitions.Left
            : named.GroupBy(d => d, StringComparer.Ordinal).OrderByDescending(g => g.Count()).ThenBy(g => named.IndexOf(g.Key)).First().Key;
    }
}
