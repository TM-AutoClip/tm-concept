using TMConcept.Storage.Keys;

namespace TMConcept.Tests.Storage;

/// <summary>Records secret-tool calls; lookups answer <see cref="Stored"/>.</summary>
internal sealed class FakeCommandRunner : ICommandRunner
{
    public List<(IReadOnlyList<string> Arguments, string? Input)> Calls { get; } = [];

    public string Stored { get; set; } = string.Empty;

    /// <summary>The exit code of <c>clear</c> (0: removed).</summary>
    public int ClearExitCode { get; set; }

    public bool Exists(string program) => true;

    public Task<(int ExitCode, string Output, string Error)> RunAsync(string program, IReadOnlyList<string> arguments, string? input,
        CancellationToken cancellationToken)
    {
        Calls.Add((arguments, input));
        return Task.FromResult(arguments[0] == "clear" && ClearExitCode != 0
            ? (ClearExitCode, string.Empty, "No such secret collection")
            : (0, arguments[0] == "lookup" ? Stored : string.Empty, string.Empty));
    }
}
