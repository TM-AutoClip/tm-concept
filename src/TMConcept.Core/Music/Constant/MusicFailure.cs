namespace TMConcept.Core.Music;

/// <summary>What kind of failure a <see cref="MusicGenerationException"/> is (for the log and the tests; the message says it to the user).</summary>
public enum MusicFailure
{
    /// <summary>Any other failure of the model.</summary>
    Music,

    /// <summary>The model's process ended badly (rakkase's exit code).</summary>
    Process,

    /// <summary>The answer had no audio.</summary>
    Empty,

    /// <summary>The model is not installed, or it wrote no piece.</summary>
    Missing,

    /// <summary>Nothing was written to make music from.</summary>
    Prompt,

    /// <summary>The online model has no working API key.</summary>
    Key,

    /// <summary>The reference sound is missing or cannot be read.</summary>
    Reference,

    /// <summary>The graphics card and the main memory together do not hold the model.</summary>
    Memory,
}
