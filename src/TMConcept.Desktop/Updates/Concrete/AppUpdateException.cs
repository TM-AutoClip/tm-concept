namespace TMConcept.Desktop.Updates;

/// <summary>
/// The update was not installed (<see cref="Code"/>: "busy" (work or the speech worker is running), "not_writable" (the program's folder
/// cannot be written), "not_installable" (the offer cannot be installed by the app, see <see cref="UpdateOffer.Problem"/>)). Nothing was
/// replaced.
/// </summary>
public sealed class AppUpdateException : Exception
{
    /// <summary>Creates.</summary>
    public AppUpdateException()
    {
    }

    /// <summary>Creates.</summary>
    public AppUpdateException(string message)
        : base(message)
    {
    }

    /// <summary>Creates.</summary>
    public AppUpdateException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Why.</summary>
    public string Code { get; init; } = "not_installable";
}
