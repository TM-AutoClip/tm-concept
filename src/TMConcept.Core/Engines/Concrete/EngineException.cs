namespace TMConcept.Core.Engines;

/// <summary>An engine failed: <see cref="Code"/> is "check" (the code was rejected), "missing" (not installed) or "render".</summary>
public sealed class EngineException : Exception
{
    /// <summary>Creates.</summary>
    public EngineException()
    {
    }

    /// <summary>Creates.</summary>
    public EngineException(string message)
        : base(message)
    {
    }

    /// <summary>Creates.</summary>
    public EngineException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Failure code.</summary>
    public string Code { get; init; } = "render";

    /// <summary>Findings of the code check.</summary>
    public IReadOnlyList<string> Findings { get; init; } = [];
}
