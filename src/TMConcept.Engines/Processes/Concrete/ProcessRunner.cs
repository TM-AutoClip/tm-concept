using System.Diagnostics;
using Microsoft.Extensions.Logging;
using TMConcept.Core.Processes;

namespace TMConcept.Engines.Processes;

/// <summary>
/// Runs a program with a cleaned environment (names containing KEY, TOKEN, SECRET, PASSWORD, PASS, AUTH or CREDENTIAL are removed) and
/// below-normal priority; keeps the last 40 output lines for error messages. Cancel kills the whole process tree, waits until the program
/// has really exited (at most <see cref="KillWait"/>: its caller deletes the work folder and gives the graphics card back right after) and
/// writes those last lines to the log. With <see cref="ProcessStart.Heartbeat"/> a line goes to the log at every interval while it runs.
/// Every child is handed to the <see cref="ChildProcessGuardBase"/>, so it cannot outlive the app.
/// </summary>
public sealed class ProcessRunner(ChildProcessGuardBase children, ILogger<ProcessRunner> logger, TimeProvider? clock = null) : IProcessRunner
{
    /// <summary>
    /// How long a cancel waits for the killed program to exit (0.1.59: a CUDA program takes a few seconds to close; its folder could not be
    /// deleted 6 ms after the kill, and the next model would have measured memory it still held).
    /// </summary>
    public static readonly TimeSpan KillWait = TimeSpan.FromSeconds(15);

    /// <summary>The output lines kept (the error message's tail, the log's lines after a cancel).</summary>
    public const int KeptLines = 40;

    private const int LogCharacters = 4000;
    private const int HeartbeatLineCharacters = 300;

    private static readonly string[] SecretWords = ["KEY", "TOKEN", "SECRET", "PASSWORD", "PASS", "AUTH", "CREDENTIAL"];

    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

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
        var latest = string.Empty;
        var gate = new object();
        void Line(string? line)
        {
            if (line is null)
            {
                return;
            }

            lock (gate)
            {
                if (start.ProgressMark is not { Length: > 0 } mark || !line.StartsWith(mark, StringComparison.Ordinal))
                {
                    last.Enqueue(line);
                    if (last.Count > KeptLines)
                    {
                        last.Dequeue();
                    }
                }

                if (line.Trim().Length > 0)
                {
                    latest = line.Trim();
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
        var started = _clock.GetTimestamp();
        using var heartbeat = start.Heartbeat is { } every && every > TimeSpan.Zero
            ? _clock.CreateTimer(_ =>
            {
                string line;
                lock (gate)
                {
                    line = latest;
                }

                logger.LogInformation("{Program} still runs after {Minutes} min; its last line: {Line}", name,
                    (int)_clock.GetElapsedTime(started).TotalMinutes, line.Length == 0 ? "-" : ChildOutput.Detail(line, 1, HeartbeatLineCharacters));
            }, null, every, every)
            : null;
        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            heartbeat?.Dispose();
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                // Already gone.
            }

            var exited = await WaitForExitAsync(process).ConfigureAwait(false);
            string tail;
            lock (gate)
            {
                tail = string.Join('\n', last);
            }

            if (exited)
            {
                logger.LogInformation("{Program} was cancelled after {Ms} ms; its process tree was ended; its last output: {Lines}", name,
                    watch.ElapsedMilliseconds, ChildOutput.Detail(tail, KeptLines, LogCharacters));
            }
            else
            {
                logger.LogWarning("{Program} was cancelled after {Ms} ms; its process tree was told to end but it had not exited after {Seconds} s; its last output: {Lines}",
                    name, watch.ElapsedMilliseconds, (int)KillWait.TotalSeconds, ChildOutput.Detail(tail, KeptLines, LogCharacters));
            }

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

    /// <summary>Waits until the killed <paramref name="process"/> has exited, at most <see cref="KillWait"/>; false when it is still there.</summary>
    private async Task<bool> WaitForExitAsync(Process process)
    {
        using var limit = new CancellationTokenSource(KillWait, _clock);
        try
        {
            await process.WaitForExitAsync(limit.Token).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException)
        {
            return process.HasExited;
        }
        catch (InvalidOperationException)
        {
            return true;
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
