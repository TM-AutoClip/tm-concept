namespace TMConcept.Core.Styles;

/// <summary>
/// A new template would make more than <see cref="VideoStyleLimits.Max"/> (<see cref="IVideoStyleStore.SaveAsync"/>); nothing was written.
/// Its own type, so Settings names the limit only for this case and not for any other <see cref="InvalidOperationException"/> (a locked
/// database, an upgrade or restore in progress).
/// </summary>
public sealed class VideoStyleLimitException : InvalidOperationException
{
    /// <summary>Creates.</summary>
    public VideoStyleLimitException()
        : base($"There are already {VideoStyleLimits.Max} templates; delete one first.")
    {
    }

    /// <summary>Creates.</summary>
    public VideoStyleLimitException(string message)
        : base(message)
    {
    }

    /// <summary>Creates.</summary>
    public VideoStyleLimitException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
