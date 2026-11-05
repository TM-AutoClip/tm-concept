namespace TMConcept.Core.Speech;

/// <summary>Narration failed (<see cref="Code"/>: "missing", "model", "engine").</summary>
public sealed class SpeechException : Exception
{
    /// <summary>Creates.</summary>
    public SpeechException()
    {
    }

    /// <summary>Creates.</summary>
    public SpeechException(string message)
        : base(message)
    {
    }

    /// <summary>Creates.</summary>
    public SpeechException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Failure code.</summary>
    public string Code { get; init; } = "engine";
}
