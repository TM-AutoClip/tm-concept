namespace TMConcept.Desktop.Catalog;

/// <summary>
/// What the code knows about a model besides its catalog entry, for its notes (<see cref="ModelNotes"/>). Every value is optional: an unknown
/// fact is left out of the notes, never guessed.
/// </summary>
public sealed record ModelFacts
{
    /// <summary>No extra facts.</summary>
    public static ModelFacts None { get; } = new();

    /// <summary>The content languages it speaks (a voice: from its engine); null = not known (text and image models).</summary>
    public IReadOnlyList<string>? Languages { get; init; }

    /// <summary>Whether it also speaks with the user's own voice samples (a Python voice says so); null = not known.</summary>
    public bool? CopiesVoice { get; init; }

    /// <summary>The size of its download in bytes (its pinned files); null = not known.</summary>
    public long? DownloadBytes { get; init; }

    /// <summary>
    /// The graphics card memory of the card it would run on (bytes); null when that card's memory is not known or there is no card
    /// (<see cref="NoCard"/>).
    /// </summary>
    public long? CardBytes { get; init; }

    /// <summary>It would run on the processor: there is no card it can use (only said when this was looked at).</summary>
    public bool NoCard { get; init; }

    /// <summary>The longest context it was trained with (tokens; the app's own Ollama models); 0 = not known.</summary>
    public long TrainedContextTokens { get; init; }

    /// <summary>It can think before it answers (the app's own Ollama models say so); null = not known.</summary>
    public bool? CanThink { get; init; }
}
