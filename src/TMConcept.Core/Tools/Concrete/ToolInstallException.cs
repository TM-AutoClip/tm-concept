namespace TMConcept.Core.Tools;

/// <summary>
/// A tool could not be installed (<see cref="Code"/>: "hash", "host", "download", "gone" (the pinned file was removed), "extract", "missing",
/// "npm", "unavailable" (the pinned file is gone or wrong and the system has no copy either)).
/// </summary>
public sealed class ToolInstallException : Exception
{
    /// <summary>Creates.</summary>
    public ToolInstallException()
    {
    }

    /// <summary>Creates.</summary>
    public ToolInstallException(string message)
        : base(message)
    {
    }

    /// <summary>Creates.</summary>
    public ToolInstallException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Failure code.</summary>
    public string Code { get; init; } = "download";
}
