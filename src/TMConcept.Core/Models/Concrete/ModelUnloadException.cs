namespace TMConcept.Core.Models;

/// <summary>
/// A local model could not be confirmed unloaded (its server refused, still lists it after the wait, or cannot be asked at all): it may still
/// hold the graphics card. <see cref="IGpuCoordinator"/> keeps it in mind, tries again at the next change of model, and the next model plans
/// with the free memory measured meanwhile. The message says why.
/// </summary>
public sealed class ModelUnloadException : Exception
{
    /// <summary>Creates.</summary>
    public ModelUnloadException()
    {
    }

    /// <summary>With a message.</summary>
    public ModelUnloadException(string message)
        : base(message)
    {
    }

    /// <summary>With a message and an inner error.</summary>
    public ModelUnloadException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
