namespace TMConcept.Core.Scenes;

/// <summary>Scenes of a concept and the problems found (an empty problem list = usable).</summary>
public sealed record SceneSplit(IReadOnlyList<Scene> Scenes, IReadOnlyList<string> Problems)
{
    /// <summary>Whether the scenes can be produced.</summary>
    public bool IsValid => Problems.Count == 0;

    /// <summary>The whole narration (scenes joined by blank lines).</summary>
    public string Narration => string.Join("\n\n", Scenes.Select(s => s.Narration).Where(n => n.Length > 0));
}
