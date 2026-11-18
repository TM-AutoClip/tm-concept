namespace TMConcept.Core.Documents;

/// <summary>
/// Text extraction stopped: <see cref="DocumentLimits.RecognitionFailuresInARowMax"/> pages failed in a row (the last failure is the inner
/// exception), or a failure that every further page would repeat (the key, the balance).
/// </summary>
public sealed class PageRecognitionStoppedException : Exception
{
    /// <summary>Creates it.</summary>
    public PageRecognitionStoppedException()
        : this("Text extraction stopped.")
    {
    }

    /// <summary>Creates it with a message.</summary>
    public PageRecognitionStoppedException(string message)
        : base(message)
    {
    }

    /// <summary>Creates it with a message and the last failure.</summary>
    public PageRecognitionStoppedException(string message, Exception inner)
        : base(message, inner)
    {
    }
}
