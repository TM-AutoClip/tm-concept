namespace TMConcept.Core.Music;

/// <summary>A music model could not make the piece (0.1.46, C5): its process ended badly, its answer had no audio, or its file is missing.</summary>
public sealed class MusicGenerationException : Exception
{
    /// <summary>Creates.</summary>
    public MusicGenerationException()
    {
    }

    /// <summary>Creates with a message.</summary>
    public MusicGenerationException(string message)
        : base(message)
    {
    }

    /// <summary>Creates with a message and its cause.</summary>
    public MusicGenerationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>What kind of failure it is.</summary>
    public MusicFailure Code { get; init; } = MusicFailure.Music;
}
