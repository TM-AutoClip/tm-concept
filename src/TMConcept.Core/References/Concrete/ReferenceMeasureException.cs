namespace TMConcept.Core.References;

/// <summary>
/// A reference video could not be measured (<see cref="Code"/>: "address" (not a web address), "missing" (yt-dlp is not installed), "download"
/// (yt-dlp failed), "file" (the file is not there), "unreadable" (ffmpeg could not read its picture), "short" (shorter than
/// <see cref="ReferenceLimits.MinSeconds"/>)). The message is in English for the log; the interface shows its own text for the code with
/// <see cref="Detail"/>.
/// </summary>
public sealed class ReferenceMeasureException : Exception
{
    /// <summary>Creates.</summary>
    public ReferenceMeasureException()
    {
    }

    /// <summary>Creates.</summary>
    public ReferenceMeasureException(string message)
        : base(message)
    {
    }

    /// <summary>Creates.</summary>
    public ReferenceMeasureException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Failure code.</summary>
    public string Code { get; init; } = "unreadable";

    /// <summary>What the tool said (its last lines, folders removed), or empty.</summary>
    public string Detail { get; init; } = string.Empty;
}
