namespace TMConcept.Storage.Crypto;

/// <summary>
/// A sealed file could not be opened. A wrong password and a damaged file are deliberately not told apart (AES-GCM cannot tell them apart
/// either; telling them apart would help guessing).
/// </summary>
public sealed class SealedBoxException : Exception
{
    /// <summary>Creates.</summary>
    public SealedBoxException()
    {
    }

    /// <summary>With a message.</summary>
    public SealedBoxException(string message)
        : base(message)
    {
    }

    /// <summary>With a message and the inner error.</summary>
    public SealedBoxException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
