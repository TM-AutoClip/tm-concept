using System.Diagnostics;
using Microsoft.Extensions.Logging;
using TMConcept.Core.Processes;

namespace TMConcept.Engines.Processes;

/// <summary>
/// Runs a program with a cleaned environment (names containing KEY, TOKEN, SECRET, PASSWORD, PASS, AUTH or CREDENTIAL are removed) and
/// below-normal priority; keeps the last 40 output lines for error messages. Cancel kills the whole process tree. Every child is handed to
/// the <see cref="ChildProcessGuardBase"/>, so it cannot outlive the app.
/// </summary>
public sealed class ProcessRunner(ChildProcessGuardBase children, ILogger<ProcessRunner> logger) : IProcessRunner
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
        foreach (var (variable, value) in start.Environment)
        {
            info.Environment[variable] = value;
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

        var name = Path.GetFileName(start.Program);
        var watch = Stopwatch.StartNew();
        using var process = new Process { StartInfo = info };
        process.OutputDataReceived += (_, e) => Line(e.Data);
        process.ErrorDataReceived += (_, e) => Line(e.Data);
        if (!process.Start())
        {
            throw new InvalidOperationException("The program could not be started: " + Path.GetFileName(start.Program));
        }

        using var adoption = children.Adopt(process);
        logger.LogInformation("{Program} started (pid {Pid})", name, process.Id);

        try
        {
            process.PriorityClass = ProcessPriorityClass.BelowNormal;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or PlatformNotSupportedException)
        {
            logger.LogWarning(ex, "The priority of {Program} could not be lowered; it runs at normal priority", name);
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

            logger.LogInformation("{Program} was cancelled after {Ms} ms; its process tree was ended", name, watch.ElapsedMilliseconds);
            throw;
        }

        process.WaitForExit();
        if (process.ExitCode == 0)
        {
            logger.LogInformation("{Program} ended (exit code 0) after {Ms} ms", name, watch.ElapsedMilliseconds);
        }
        else
        {
            logger.LogWarning("{Program} ended with exit code {Exit} after {Ms} ms", name, process.ExitCode, watch.ElapsedMilliseconds);
        }

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
