namespace TMConcept.Core.Plans;

/// <summary>
/// Two beats in a row may not show the same shot: the same background with the camera on the same point at the same zoom while nothing new
/// happens; and a scene may not open on the shot the previous scene ended on (then the cut changes nothing). The camera of a beat without
/// one stays where it was; every scene starts on the whole frame.
/// </summary>
public sealed class RepeatedShotRule : PlanRuleBase
{
    /// <inheritdoc />
    protected override IEnumerable<string> Find(IReadOnlyList<PlanItem> items, PlanCheck check)
    {
        Shot? last = null;
        foreach (var (scene, plan) in Scenes(items, check))
        {
            var background = BackgroundOf(plan, scene, check);
            var beats = plan.Beats.OrderBy(b => b.Sentence).ToList();
            var opening = beats.FirstOrDefault(b => b.Sentence <= 1);
            var shot = opening?.Camera is { } first ? new Shot(background, first.Focus, first.Zoom) : new Shot(background, string.Empty, 1);
            if (last is { } previous && previous.Same(shot))
            {
                yield return string.Create(C,
                    $"scene {scene} opens on the shot scene {scene - 1} ends on (same background and camera): change its base, region or camera");
            }

            foreach (var beat in beats.Where(b => !ReferenceEquals(b, opening)))
            {
                var next = beat.Camera is { } camera ? new Shot(background, camera.Focus, camera.Zoom) : shot;
                if (shot.Same(next) && beat.Actions.Count == 0)
                {
                    yield return string.Create(C,
                        $"scene {scene}, sentence {beat.Sentence}: the beat repeats the previous shot with nothing new; move the camera, add an action or leave the sentence without a beat");
                }

                shot = next;
            }

            last = shot;
        }
    }

    /// <summary>What the viewer sees: the background and where the camera is.</summary>
    private readonly record struct Shot(string Background, string Focus, double Zoom)
    {
        public bool Same(Shot other) => Background == other.Background && string.Equals(Focus, other.Focus, StringComparison.Ordinal)
            && Math.Abs(Zoom - other.Zoom) < PlanLimits.SameZoom;
    }
}
