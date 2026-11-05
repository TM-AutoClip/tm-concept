using System.Diagnostics;
using Microsoft.Extensions.Logging;
using TMConcept.Core.Processes;

namespace TMConcept.Engines.Processes;

/// <summary>
/// Runs a program with a cleaned environment (names containing KEY, TOKEN, SECRET, PASSWORD, PASS, AUTH or CREDENTIAL are removed) and
/// below-normal priority; keeps the last 40 output lines for error messages. Cancel kills the whole process tree.
/// </summary>
public sealed class ProcessRunner(ILogger<ProcessRunner> logger) : IProcessRunner
{
    private static readonly string[] SecretWords = ["KEY", "TOKEN", "SECRET", "PASSWORD", "PASS", "AUTH", "CREDENTIAL"];

    /// <inheritdoc />
    public async Task<ProcessResult> RunAsync(ProcessStart start, Action<string>? onLine, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(start);
        var info = new ProcessStartInfo(start.Program)
        {
            WorkingDirectory = start.WorkingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8,
        };
        foreach (var argument in start.Arguments)
        {
            info.ArgumentList.Add(argument);
        }

        Clean(info.Environment);
        foreach (var (name, value) in start.Environment)
        {
            info.Environment[name] = value;
        }

        var last = new Queue<string>();
        var gate = new object();
        void Line(string? line)
        {
            if (line is null)
            {
                return;
            }

            lock (gate)
            {
                last.Enqueue(line);
                if (last.Count > 40)
                {
                    last.Dequeue();
                }
            }

            onLine?.Invoke(line);
        }

        using var process = new Process { StartInfo = info };
        process.OutputDataReceived += (_, e) => Line(e.Data);
        process.ErrorDataReceived += (_, e) => Line(e.Data);
        if (!process.Start())
        {
            throw new InvalidOperationException("The program could not be started: " + Path.GetFileName(start.Program));
        }

        try
        {
            process.PriorityClass = ProcessPriorityClass.BelowNormal;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or PlatformNotSupportedException)
        {
            logger.LogDebug(ex, "The priority of {Program} could not be lowered", Path.GetFileName(start.Program));
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                // Already gone.
            }

            throw;
        }

        process.WaitForExit();
        lock (gate)
        {
            return new ProcessResult(process.ExitCode, string.Join('\n', last));
        }
    }

    /// <summary>Removes secret-looking variables.</summary>
    public static void Clean(IDictionary<string, string?> environment)
    {
        ArgumentNullException.ThrowIfNull(environment);
        foreach (var name in environment.Keys.ToList())
        {
            if (SecretWords.Any(w => name.Contains(w, StringComparison.OrdinalIgnoreCase)))
            {
                environment.Remove(name);
            }
        }
    }
}
