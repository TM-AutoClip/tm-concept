namespace TMConcept.Core.Videos;

/// <summary>
/// A video could not be made (<see cref="Code"/>: "scenes", "code", "render", "mux", "files", "missing", or <see cref="ProviderPrefix"/> +
/// the provider's failure code).
/// </summary>
public sealed class VideoException : Exception
{
    /// <summary>Start of <see cref="Code"/> when a provider call failed ("provider:truncated").</summary>
    public const string ProviderPrefix = "provider:";

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
