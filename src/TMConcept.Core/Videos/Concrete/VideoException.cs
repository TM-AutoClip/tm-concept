namespace TMConcept.Core.Videos;

/// <summary>A video could not be made (<see cref="Code"/>: "scenes", "code", "render", "mux").</summary>
public sealed class VideoException : Exception
{
    /// <summary>Creates.</summary>
    public VideoException()
    {
    }

    /// <summary>Creates.</summary>
    public VideoException(string message)
        : base(message)
    {
    }

    /// <summary>Creates.</summary>
    public VideoException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Failure code.</summary>
    public string Code { get; init; } = "render";

    /// <summary>The cost spent before the failure.</summary>
    public decimal SpentUsd { get; init; }
}
