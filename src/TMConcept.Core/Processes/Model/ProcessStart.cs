namespace TMConcept.Core.Processes;

/// <summary>What to run.</summary>
public sealed record ProcessStart(string Program, IReadOnlyList<string> Arguments, string WorkingDirectory)
{
    /// <summary>Extra environment variables (after the clean-up).</summary>
    public IReadOnlyDictionary<string, string> Environment { get; init; } = new Dictionary<string, string>();
}
