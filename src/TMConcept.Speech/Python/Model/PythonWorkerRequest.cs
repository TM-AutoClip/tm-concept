namespace TMConcept.Speech.Python;

/// <summary>
/// One line to the Python worker (<c>worker.py</c>): <c>load</c> (adapter, model folder, device, free graphics memory it needs, threads,
/// options), <c>synth</c> (text, language, voice, reference clip, speed, output WAV) or <c>quit</c>. Unused fields are left out of the line.
/// </summary>
public sealed record PythonWorkerRequest(string Cmd)
{
    /// <summary>The adapter (<c>adapter_&lt;name&gt;.py</c>).</summary>
    public string? Adapter { get; init; }

    /// <summary>The model folder.</summary>
    public string? Model { get; init; }

    /// <summary><c>cuda</c> or <c>cpu</c>.</summary>
    public string? Device { get; init; }

    /// <summary>Free graphics memory the model needs on the card (MB); with less the worker uses the processor.</summary>
    public int? NeedMb { get; init; }

    /// <summary>Processor threads.</summary>
    public int? Threads { get; init; }

    /// <summary>Adapter options.</summary>
    public IReadOnlyDictionary<string, string>? Options { get; init; }

    /// <summary>The text to speak.</summary>
    public string? Text { get; init; }

    /// <summary>Content language.</summary>
    public string? Language { get; init; }

    /// <summary>The voice (a built-in speaker, an emotion; empty: the default).</summary>
    public string? Voice { get; init; }

    /// <summary>A reference clip to copy the voice of (the user's sample, written for this request).</summary>
    public string? Reference { get; init; }

    /// <summary>Speed (1 = normal).</summary>
    public double? Speed { get; init; }

    /// <summary>The WAV file to write.</summary>
    public string? Out { get; init; }

    /// <summary>The <c>quit</c> line.</summary>
    public static PythonWorkerRequest Quit { get; } = new("quit");
}
