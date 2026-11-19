using TMConcept.Core.Profiles;
using TMConcept.Core.Scenes;

namespace TMConcept.Core.Concepts;

/// <summary>
/// The deterministic check of a concept's scenes (20 Nov 2026; a small local model wrote a whole concept without one scene line and the
/// video showed nothing but a coloured background): at least <see cref="Minimum"/> scenes, every scene says what is on screen, and no scene's
/// narration runs longer than <see cref="SceneTargets.MaxSceneSeconds"/>. It reads the stored text with <see cref="SceneSplitter"/>, the same
/// way the video does, so a concept made by hand or edited later is judged exactly like a generated one. No model call.
/// </summary>
public static class ConceptSceneCheck
{
    /// <summary>The scene count the prompt asks for at <paramref name="seconds"/> (one every <see cref="SceneTargets.SecondsPerScene"/> s, at least 1).</summary>
    public static int Target(int seconds) =>
        Math.Max(1, (int)Math.Round(Math.Clamp(seconds, ProfileLimits.SecondsMin, ProfileLimits.SecondsMax) / SceneTargets.SecondsPerScene, MidpointRounding.AwayFromZero));

    /// <summary>The fewest scenes the check accepts at <paramref name="seconds"/> (the target less its tolerance, at least 1).</summary>
    public static int Minimum(int seconds)
    {
        var target = Target(seconds);
        return Math.Max(1, target - (int)Math.Ceiling(target * SceneTargets.ToleranceShare));
    }

    /// <summary>The most narration words of one scene in <paramref name="language"/>.</summary>
    public static int MaxSceneWords(string language) => (int)Math.Floor(SceneTargets.MaxSceneSeconds * OutputLanguages.Of(language).WordsPerSecond);

    /// <summary>The findings of <paramref name="content"/> (empty = passed) for a video of <paramref name="seconds"/> in <paramref name="language"/>.</summary>
    public static IReadOnlyList<SceneProblem> Check(string? content, string language, int seconds)
    {
        var scenes = SceneSplitter.Split(content).Scenes;
        var problems = new List<SceneProblem>();
        var minimum = Minimum(seconds);
        if (scenes.Count < minimum)
        {
            problems.Add(new SceneProblem(SceneProblemKind.TooFew, 0, scenes.Count, minimum, Target(seconds)));
        }

        var rate = OutputLanguages.Of(language).WordsPerSecond;
        foreach (var scene in scenes)
        {
            if (scene.Visual.Trim().Length == 0)
            {
                problems.Add(new SceneProblem(SceneProblemKind.EmptyVisual, scene.Number, 0, 0));
            }

            var spoken = scene.Narration.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length / rate;
            if (spoken > SceneTargets.MaxSceneSeconds)
            {
                problems.Add(new SceneProblem(SceneProblemKind.LongNarration, scene.Number, (int)Math.Round(spoken), (int)SceneTargets.MaxSceneSeconds));
            }
        }

        return problems;
    }
}
