namespace TMConcept.Storage.Keys;

/// <summary>Runs a command line program with arguments (never through a shell) and optional standard input (secrets never go in arguments).</summary>
public interface ICommandRunner
{
    /// <summary>Whether the program is on the PATH.</summary>
    bool Exists(string program);

    /// <summary>Runs; returns the exit code and standard output.</summary>
    Task<(int ExitCode, string Output, string Error)> RunAsync(string program, IReadOnlyList<string> arguments, string? input, CancellationToken cancellationToken);
}
