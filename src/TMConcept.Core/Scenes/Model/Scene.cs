namespace TMConcept.Core.Scenes;

/// <summary>
/// A scene of a concept: what is shown (the bracket line) and what is said (the narration until the next bracket line, without any
/// placeholder: spoken, shown as subtitles, counted). <see cref="Script"/> is the same narration with its narration placeholders (pauses,
/// speed) where they stand, for the voice; <see cref="Transition"/> is how the scene begins (<see cref="SceneTransitions"/>).
/// </summary>
public sealed record Scene(int Number, string Visual, string Narration)
{
    private readonly string? _script;

    /// <summary>The narration with its narration placeholders (the narration itself when it has none).</summary>
    public string Script
    {
        get => _script ?? Narration;
        init => _script = value;
    }

    /// <summary>How the scene begins (empty = no placeholder; the first scene never has one).</summary>
    public string Transition { get; init; } = SceneTransitions.None;
}
