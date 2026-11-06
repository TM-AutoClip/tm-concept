using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace TMConcept.Storage.Keys;

/// <summary>
/// <see cref="ICommandRunner"/> with <see cref="Process"/> (Linux: <c>secret-tool</c>, <c>gio</c>). Each call is one log line with the
/// program, its first argument (the command: <c>store</c>, <c>lookup</c>, <c>clear</c>, <c>trash</c>), the exit code and the duration;
/// never the other arguments (paths, keyring ids), the input (a key) or the output. The caller writes what a failure means.
/// </summary>
public sealed class ProcessCommandRunner(ILogger<ProcessCommandRunner>? logger = null) : ICommandRunner
{
    private readonly ILogger _logger = logger ?? NullLogger<ProcessCommandRunner>.Instance;

    /// <inheritdoc />
    public bool Exists(string program)
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        return path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries).Any(d => File.Exists(Path.Combine(d, program)));
    }

    /// <inheritdoc />
    public async Task<(int ExitCode, string Output, string Error)> RunAsync(string program, IReadOnlyList<string> arguments, string? input,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        var command = arguments.Count > 0 ? arguments[0] : string.Empty;
        var info = new ProcessStartInfo(program)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        var watch = Stopwatch.StartNew();
        Process process;
        try
        {
            process = Process.Start(info) ?? throw new InvalidOperationException($"{program} could not be started.");
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            _logger.LogWarning(ex, "{Program} {Command} could not be started", program, command);
            throw;
        }

        using (process)
        {
            if (input is not null)
            {
                await process.StandardInput.WriteAsync(input.AsMemory(), cancellationToken).ConfigureAwait(false);
            }

            process.StandardInput.Close();
            var output = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var error = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            var result = (process.ExitCode, await output.ConfigureAwait(false), await error.ConfigureAwait(false));
            if (result.ExitCode == 0)
            {
                _logger.LogInformation("{Program} {Command} exited 0 in {Ms} ms", program, command, watch.ElapsedMilliseconds);
            }
            else
            {
                _logger.LogWarning("{Program} {Command} exited {Code} in {Ms} ms", program, command, result.ExitCode, watch.ElapsedMilliseconds);
            }

            return result;
        }
    }
}
