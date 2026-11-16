using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using TMConcept.Core.Processes;
using TMConcept.Core.Speech;

namespace TMConcept.Speech.Python;

/// <summary>
/// Runs the Python worker of the Python voices (<c>worker.py</c>): at most one worker for the whole app (one Python voice is loaded at a time,
/// like one model on the graphics card). It starts on the first request of a voice (<see cref="PythonWorkerStart"/>: the environment's
/// interpreter in isolated mode, variables that look like secrets removed, below-normal priority, tied to the app), loads the model once
/// (<c>load</c>), then speaks one request at a time. Another voice or device ends it first. It ends when the voice is unloaded
/// (<see cref="StopAsync"/>: the graphics card coordinator, or the end of the narration step) and after <see cref="IdleTimeout"/> without a
/// request. A cancel kills it at once (the next request starts it again). Its error output is the libraries' chatter: kept for the log of a
/// failure, and lines the worker marks as warnings are logged as warnings.
/// </summary>
public sealed class PythonWorkerClient(IChildProcessGuard children, ILogger<PythonWorkerClient> logger) : IDisposable
{
    /// <summary>The prefix of the worker's warning lines.</summary>
    public const string WarningPrefix = "[tmc:warn]";

    private static readonly string[] SecretWords = ["KEY", "TOKEN", "SECRET", "PASSWORD", "PASS", "AUTH", "CREDENTIAL"];

    // Never disposed (as in SpeechWorkerClient): a late Release while the app closes must not throw.
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Lock _sync = new();
    private readonly Queue<string> _errors = new();
    private Process? _process;
    private IDisposable? _adoption;
    private string _key = string.Empty;
    private PythonWorkerReply? _loaded;
    private Timer? _idle;
    private volatile bool _disposed;

    /// <summary>How long a worker without requests stays (it holds the model in memory and maybe on the card).</summary>
    public TimeSpan IdleTimeout { get; init; } = TimeSpan.FromSeconds(90);

    /// <summary>The key of the running worker (voice and device); empty when none runs.</summary>
    public string RunningKey
    {
        get
        {
            lock (_sync)
            {
                return _process is { HasExited: false } ? _key : string.Empty;
            }
        }
    }

    /// <summary>What the running worker answered to its <c>load</c> (where the model runs); null when none runs.</summary>
    public PythonWorkerReply? Loaded
    {
        get
        {
            lock (_sync)
            {
                return _process is { HasExited: false } ? _loaded : null;
            }
        }
    }

    /// <summary>
    /// Speaks <paramref name="request"/> with the worker of <paramref name="start"/> (started and loaded first when it is not the running one).
    /// A model that cannot be loaded gives a reply with code <c>model</c> (the worker is ended).
    /// </summary>
    public async Task<PythonWorkerReply> SpeakAsync(PythonWorkerStart start, PythonWorkerRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(start);
        ArgumentNullException.ThrowIfNull(request);
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _idle?.Change(Timeout.Infinite, Timeout.Infinite);
            var process = Running(start.Key);
            if (process is null)
            {
                process = Start(start);
                var load = await ExchangeAsync(process, start.Load, null, cancellationToken).ConfigureAwait(false);
                if (!load.Ok)
                {
                    logger.LogError("The Python voice could not be loaded ({Code}): {Error}; its last messages: {Errors}", load.Code, load.Error, LastErrors());
                    Stop("load failed");
                    return load with { Code = load.Code.Length > 0 ? load.Code : "model" };
                }

                lock (_sync)
                {
                    _loaded = load;
                }

                if (load.Note.Length > 0)
                {
                    logger.LogWarning("The Python voice runs on the processor although the graphics card was asked for: {Note}", load.Note);
                }

                logger.LogInformation("Python voice loaded ({Device})", load.Device);
            }

            var reply = await ExchangeAsync(process, request, request.Out, cancellationToken).ConfigureAwait(false);
            if (!reply.Ok)
            {
                logger.LogError("The Python voice could not speak ({Code}): {Error}; its last messages: {Errors}", reply.Code, reply.Error, LastErrors());
            }

            return reply;
        }
        finally
        {
            if (!_disposed && RunningKey.Length > 0)
            {
                _idle ??= new Timer(_ => StopWhenIdle());
                _idle.Change(IdleTimeout, Timeout.InfiniteTimeSpan);
            }

            _gate.Release();
        }
    }

    /// <summary>
    /// Ends the worker when it runs for <paramref name="keyPrefix"/> (a voice code: every device), after the request being spoken (if any) is
    /// done. Never throws (a cancel leaves it running).
    /// </summary>
    public async Task StopAsync(string keyPrefix, CancellationToken cancellationToken)
    {
        try
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        try
        {
            if (RunningKey.StartsWith(keyPrefix + "|", StringComparison.Ordinal))
            {
                Stop("unloaded");
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Ends a worker that waits for work now (a components reinstall); false when a request is being spoken.</summary>
    public bool StopIfIdle()
    {
        if (!_gate.Wait(0))
        {
            return false;
        }

        try
        {
            Stop("components reinstall");
            return true;
        }
        finally
        {
            _gate.Release();
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
        _idle?.Dispose();
        Stop("closing");
        if (_gate.Wait(TimeSpan.FromSeconds(10)))
        {
            _gate.Release();
        }
    }

    /// <summary>The environment of a worker process: the app's own, without variables that look like secrets, plus <paramref name="extra"/>.</summary>
    public static void Clean(IDictionary<string, string?> environment, IReadOnlyDictionary<string, string> extra)
    {
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(extra);
        foreach (var name in environment.Keys.Where(n => SecretWords.Any(w => n.Contains(w, StringComparison.OrdinalIgnoreCase))).ToList())
        {
            environment.Remove(name);
        }

        foreach (var (name, value) in extra)
        {
            environment[name] = value;
        }
    }

    private Process? Running(string key)
    {
        lock (_sync)
        {
            if (_process is { HasExited: false } running && _key == key)
            {
                return running;
            }
        }

        Stop("another voice");
        return null;
    }

    private async Task<PythonWorkerReply> ExchangeAsync(Process process, PythonWorkerRequest request, string? output, CancellationToken cancellationToken)
    {
        string? answer;
        try
        {
            using (cancellationToken.Register(() => Kill(process)))
            {
                await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(request, PythonJsonContext.Default.PythonWorkerRequest).AsMemory(),
                    cancellationToken).ConfigureAwait(false);
                await process.StandardInput.FlushAsync(cancellationToken).ConfigureAwait(false);
                answer = await process.StandardOutput.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            }

            cancellationToken.ThrowIfCancellationRequested();
        }
        catch (Exception ex) when (ex is OperationCanceledException || (ex is IOException && cancellationToken.IsCancellationRequested))
        {
            Stop("cancelled");
            Remove(output);
            throw new OperationCanceledException(cancellationToken);
        }
        catch (IOException ex)
        {
            logger.LogWarning(ex, "The Python worker's pipe broke");
            answer = null;
        }
        catch (Exception ex) when (_disposed && ex is ObjectDisposedException or InvalidOperationException)
        {
            throw new SpeechException("The voice process was ended because the app is closing.", ex) { Code = "engine" };
        }

        if (answer is null)
        {
            var exit = ExitCode(process);
            var errors = LastErrors();
            Stop("ended");
            logger.LogError("The Python worker ended unexpectedly (exit code {Exit}); its last messages: {Errors}", exit, errors);
            throw new SpeechException($"The voice process ended unexpectedly (exit code {exit}). {errors}".Trim()) { Code = "engine" };
        }

        try
        {
            return JsonSerializer.Deserialize(answer, PythonJsonContext.Default.PythonWorkerReply)
                ?? throw new SpeechException("The voice process gave an empty answer.") { Code = "engine" };
        }
        catch (JsonException ex)
        {
            logger.LogError(ex, "The Python worker's answer could not be read");
            Stop("unreadable");
            throw new SpeechException("The voice process gave an unreadable answer.", ex) { Code = "engine" };
        }
    }

    private Process Start(PythonWorkerStart start)
    {
        var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        var info = new ProcessStartInfo(start.Program)
        {
            WorkingDirectory = start.WorkingDirectory,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardInputEncoding = utf8,
            StandardOutputEncoding = utf8,
            StandardErrorEncoding = utf8,
        };
        foreach (var argument in start.Arguments)
        {
            info.ArgumentList.Add(argument);
        }

        Clean(info.Environment, start.Environment);
        var process = new Process { StartInfo = info };
        process.ErrorDataReceived += (_, e) => Error(e.Data);
        lock (_errors)
        {
            _errors.Clear();
        }

        try
        {
            if (!process.Start())
            {
                throw new SpeechException("The voice process could not be started.") { Code = "engine" };
            }
        }
        catch (Win32Exception ex)
        {
            process.Dispose();
            logger.LogError(ex, "The Python worker could not be started");
            throw new SpeechException("The voice process could not be started: " + ex.Message, ex) { Code = "engine" };
        }

        var adoption = children.Adopt(process);
        try
        {
            process.PriorityClass = ProcessPriorityClass.BelowNormal;
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or PlatformNotSupportedException)
        {
            logger.LogWarning(ex, "The priority of the Python worker could not be lowered; it runs at normal priority");
        }

        process.BeginErrorReadLine();
        lock (_sync)
        {
            _process = process;
            _adoption = adoption;
            _key = start.Key;
            _loaded = null;
        }

        logger.LogInformation("Python worker started for {Key} (pid {Pid})", start.Key, process.Id);
        return process;
    }

    private void StopWhenIdle()
    {
        if (_disposed || !_gate.Wait(0))
        {
            return;
        }

        try
        {
            Stop("idle");
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Ends the worker: it is asked to quit (its input closed); after 10 seconds it is killed.</summary>
    private void Stop(string reason)
    {
        Process? process;
        IDisposable? adoption;
        lock (_sync)
        {
            process = _process;
            adoption = _adoption;
            _process = null;
            _adoption = null;
            _key = string.Empty;
            _loaded = null;
        }

        if (process is null)
        {
            return;
        }

        try
        {
            if (!process.HasExited)
            {
                try
                {
                    process.StandardInput.Close();
                }
                catch (IOException ex)
                {
                    logger.LogInformation(ex, "The Python worker's input could not be closed ({Reason}); it is ending already", reason);
                }

                if (!process.WaitForExit(10_000))
                {
                    logger.LogWarning("The Python worker did not end within 10 seconds ({Reason}); it is killed", reason);
                    process.Kill(entireProcessTree: true);
                    process.WaitForExit(5_000);
                }
            }

            logger.LogInformation("Python worker ended ({Reason})", reason);
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or IOException)
        {
            logger.LogWarning(ex, "The Python worker could not be ended cleanly ({Reason})", reason);
        }
        finally
        {
            adoption?.Dispose();
            process.Dispose();
        }
    }

    private void Kill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
            logger.LogInformation("The speech request was cancelled; the Python worker is killed");
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or NotSupportedException)
        {
            logger.LogInformation(ex, "The Python worker could not be killed after a cancel (it may have ended already)");
        }
    }

    private void Remove(string? file)
    {
        try
        {
            if (file is { Length: > 0 } && File.Exists(file))
            {
                File.Delete(file);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "The sound file of a cancelled speech request could not be removed ({File})", Path.GetFileName(file));
        }
    }

    private string ExitCode(Process process)
    {
        try
        {
            return process.WaitForExit(5000) ? process.ExitCode.ToString(CultureInfo.InvariantCulture) : "?";
        }
        catch (InvalidOperationException)
        {
            return "?";
        }
    }

    private void Error(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return;
        }

        if (line.StartsWith(WarningPrefix, StringComparison.Ordinal))
        {
            logger.LogWarning("Python worker: {Line}", line[WarningPrefix.Length..].Trim());
        }
        else
        {
            logger.LogDebug("Python worker: {Line}", line);
        }

        lock (_errors)
        {
            _errors.Enqueue(line.Length > 300 ? line[..300] : line);
            if (_errors.Count > 20)
            {
                _errors.Dequeue();
            }
        }
    }

    private string LastErrors()
    {
        lock (_errors)
        {
            return string.Join(" | ", _errors);
        }
    }
}
