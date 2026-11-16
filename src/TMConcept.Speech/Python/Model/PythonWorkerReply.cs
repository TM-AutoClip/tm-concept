namespace TMConcept.Speech.Python;

/// <summary>The Python worker's answer to one line (<see cref="Code"/> of a failure: <c>model</c>, <c>engine</c>, <c>request</c>).</summary>
public sealed record PythonWorkerReply
{
    /// <summary>Whether it worked.</summary>
    public bool Ok { get; init; }

    /// <summary>Failure code.</summary>
    public string Code { get; init; } = string.Empty;

    /// <summary>Failure text.</summary>
    public string Error { get; init; } = string.Empty;

    /// <summary>Where the model runs after <c>load</c> (<c>cuda</c>, <c>cpu</c>).</summary>
    public string Device { get; init; } = string.Empty;

    /// <summary>Why it runs on the processor although the card was asked for (empty otherwise).</summary>
    public string Note { get; init; } = string.Empty;

    /// <summary>Sample rate of what it writes.</summary>
    public int SampleRate { get; init; }

    /// <summary>Length of the written clip in seconds.</summary>
    public double Duration { get; init; }
}
