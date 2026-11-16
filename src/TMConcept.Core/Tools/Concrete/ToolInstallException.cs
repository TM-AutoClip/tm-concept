namespace TMConcept.Core.Tools;

/// <summary>
/// A tool could not be installed (<see cref="Code"/>: "hash", "host", "download", "gone" (the pinned file was removed), "extract", "missing",
/// "npm", "unavailable" (the pinned file is gone or wrong and the system has no copy either); after every try of a download failed on the
/// network: "tls" (the secure connection could not be set up), "dns" (the server's name was not found), "connection" (refused, reset or
/// ended early), "timeout" (no data arrived for a while)). <see cref="Detail"/> is the inner reason as the system said it (shown on screen).
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

    /// <summary>The inner reason (the system's own words, for example the TLS or DNS error); empty when there is none.</summary>
    public string Detail { get; init; } = string.Empty;
}
