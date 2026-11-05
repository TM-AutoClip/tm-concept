namespace TMConcept.Core.Playback;

/// <summary>The sound device could not be opened or written (no device, no PulseAudio server); the player goes on without sound.</summary>
public sealed class AudioOutputException : Exception
{
    /// <summary>Creates.</summary>
    public AudioOutputException()
    {
    }

    /// <summary>Creates.</summary>
    public AudioOutputException(string message)
        : base(message)
    {
    }

    /// <summary>Creates.</summary>
    public AudioOutputException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
