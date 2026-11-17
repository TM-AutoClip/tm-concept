using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using TMConcept.Core.AppOllama;

namespace TMConcept.Engines.AppOllama;

/// <summary>
/// A running <c>ollama serve</c> of the app: keeps its last output lines (Ollama logs to standard error), logs a warning or error line of it
/// (never the whole output: it may name the user's folders), and ends the whole tree (the model runners) on <see cref="Stop"/>.
/// </summary>
public sealed class AppOllamaProcess : IAppOllamaProcess
{
    private const int Kept = 20;
    private readonly Process _process;
    private readonly ILogger _logger;
    private readonly Queue<string> _lines = new();
    private readonly Lock _gate = new();
    private IDisposable? _adoption;
    private bool _disposed;

    /// <summary>Over a process that is about to start.</summary>
    public AppOllamaProcess(Process process, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(process);
        _process = process;
        _logger = logger;
        process.OutputDataReceived += (_, e) => Add(e.Data);
        process.ErrorDataReceived += (_, e) => Add(e.Data);
    }

    /// <inheritdoc />
    public bool HasExited
    {
        get
        {
            try
            {
                return _process.HasExited;
            }
            catch (InvalidOperationException)
            {
                return true;
            }
        }
    }

    /// <inheritdoc />
    public int? ExitCode => HasExited ? SafeExitCode() : null;

    /// <inheritdoc />
    public string RecentOutput
    {
        get
        {
            lock (_gate)
            {
                return string.Join(Environment.NewLine, _lines);
            }
        }
    }

    /// <summary>Starts reading its output and keeps its tie to the app (called right after the start).</summary>
    internal void Attach(IDisposable adoption)
    {
        _adoption = adoption;
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();
    }

    /// <inheritdoc />
    public void Stop()
    {
        try
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
                _process.WaitForExit(TimeSpan.FromSeconds(10));
            }
        }
        catch (InvalidOperationException)
        {
            // It ended between the check and the kill.
        }
        catch (Exception ex) when (ex is Win32Exception or NotSupportedException or AggregateException)
        {
            _logger.LogError(ex, "{Server} could not be ended", AppOllamaDefaults.Name);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Stop();
        _adoption?.Dispose();
        _process.Dispose();
    }

    private void Add(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return;
        }

        lock (_gate)
        {
            _lines.Enqueue(line.Length > 400 ? line[..400] : line);
            while (_lines.Count > Kept)
            {
                _lines.Dequeue();
            }
        }

        if (line.Contains("level=ERROR", StringComparison.Ordinal))
        {
            _logger.LogWarning("{Server} reported an error: {Line}", AppOllamaDefaults.Name, Level(line));
        }
    }

    private static string Level(string line)
    {
        // Ollama's log line: time=… level=ERROR source=… msg="…"; only the message (paths in other fields are left out).
        var start = line.IndexOf("msg=", StringComparison.Ordinal);
        return start >= 0 ? line[(start + 4)..].Trim().Trim('"') : "(see the server output)";
    }

    private int? SafeExitCode()
    {
        try
        {
            return _process.ExitCode;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }
}
