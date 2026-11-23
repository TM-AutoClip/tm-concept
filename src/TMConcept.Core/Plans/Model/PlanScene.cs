namespace TMConcept.Core.Plans;

/// <summary>
/// What the plan call is told about one scene: its number, its length, what is on screen, its narration sentences in order and the prompt its
/// generated image was made from (empty: the scene has no image).
/// </summary>
public sealed record PlanScene(int Number, double Seconds, string Visual, IReadOnlyList<string> Sentences, string ImagePrompt);
