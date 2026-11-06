using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using TMConcept.Core.Processes;
using TMConcept.Core.Speech;

namespace TMConcept.Speech.Speech;

/// <summary>
/// Talks to the speech worker (<see cref="SpeechWorker"/>): starts the app's own program with <see cref="SpeechWorker.Argument"/> on the first
/// request, keeps it (and its loaded model) while scenes are spoken one after another, and ends it after <see cref="IdleTimeout"/> without a
/// request, so its memory goes back to the system. One request at a time. A cancelled request kills the worker at once, even in the middle
/// of a scene (the next request starts it again). The worker is tied to the app like every other child process. Its error output is logged
/// as a warning line by line, and a failure's log line carries the worker's last messages (read to their end once the worker has exited).
/// </summary>
public sealed class SpeechWorkerClient(IChildProcessGuard children, ILogger<SpeechWorkerClient> logger) : IDisposable
{
    // Never disposed: without AvailableWaitHandle it holds no unmanaged resource, and a Release that comes late (an idle stop or a request
    // still finishing while the app closes) must not throw ObjectDisposedException on a thread-pool thread.
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Lock _sync = new();
    private readonly Queue<string> _errors = new();
    private Process? _process;
    private IDisposable? _adoption;
    private string _startedFor = string.Empty;
    private Timer? _idle;
    private volatile bool _disposed;

    /// <summary>How long a worker without requests stays (it holds the voice model in memory).</summary>
    public TimeSpan IdleTimeout { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>The log folder the worker writes its own file in (<see cref="SpeechWorker.LogDirectoryVariable"/>); empty: none.</summary>
    public string LogDirectory { get; init; } = string.Empty;

    /// <summary>The program started as the worker (the app's own executable; tests give their own).</summary>
    public string ProgramPath { get; init; } = Environment.ProcessPath ?? string.Empty;

    /// <summary>Whether a worker runs now.</summary>
    public bool Running
    {
        get
        {
            lock (_sync)
            {
                return _process is { HasExited: false };
            }
        }
    }

    /// <summary>Speaks one request with the libraries of <paramref name="libraryFolder"/> and the model of <paramref name="modelFolder"/>.</summary>
    public Task<SpeechWorkerReply> SpeakAsync(string libraryFolder, string modelFolder, int threads, SpeechWorkerRequest request,
        CancellationToken cancellationToken) => RequestAsync(libraryFolder, modelFolder, threads, request, cancellationToken);

    /// <summary>
    /// Turns <paramref name="texts"/> into vectors with the local embedding model of <paramref name="modelFolder"/> (the same worker program;
    /// a worker of another model folder is ended and a new one started).
    /// </summary>
    public async Task<IReadOnlyList<float[]>> EmbedAsync(string libraryFolder, string modelFolder, int threads, IReadOnlyList<string> texts,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(texts);
        var reply = await RequestAsync(libraryFolder, modelFolder, threads, new SpeechWorkerRequest(string.Empty, string.Empty, 0, 1, 0, string.Empty, texts),
            cancellationToken).ConfigureAwait(false);
        if (!reply.Ok || reply.Vectors is not { } vectors || vectors.Count != texts.Count)
        {
            throw new SpeechException("The local embedding model failed: " + (reply.Error.Length > 0 ? reply.Error : "no vectors")) { Code = "engine" };
        }

        return vectors;
    }

    private async Task<SpeechWorkerReply> RequestAsync(string libraryFolder, string modelFolder, int threads, SpeechWorkerRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _idle?.Change(Timeout.Infinite, Timeout.Infinite);
            var process = Start(libraryFolder, modelFolder, threads);
            var answer = await ExchangeAsync(process, request, cancellationToken).ConfigureAwait(false);
            if (answer is null)
            {
                var exit = ExitCodeOnceEnded(process);
                var errors = LastErrors();
                Stop("ended");
                if (_disposed)
                {
                    logger.LogWarning("The speech worker was ended while a scene was spoken because the app is closing; its last messages: {Errors}", errors);
                    throw new SpeechException("The voice process was ended because the app is closing.") { Code = "engine" };
                }

                logger.LogError("The speech worker ended unexpectedly (exit code {Exit}); its last messages: {Errors}", exit, errors);
                throw new SpeechException($"The voice process ended unexpectedly (exit code {exit}). {errors}".Trim()) { Code = "engine" };
            }

            var reply = JsonSerializer.Deserialize(answer, SpeechWorkerJsonContext.Default.SpeechWorkerReply)
                ?? throw new SpeechException("The voice process gave an empty answer.") { Code = "engine" };
            if (!reply.Ok)
            {
                if (reply.Code == "model")
                {
                    // The worker stops after a model or library failure; once it has ended its error output (sherpa-onnx's own reason) is
                    // read to the end, so the log line below has it. The two answers come on separate pipes and may arrive in any order.
                    Stop("failed");
                }

                logger.LogError("The speech worker could not speak ({Code}): {Error}; its last messages: {Errors}", reply.Code, reply.Error, LastErrors());
            }

            return reply;
        }
        catch (JsonException ex)
        {
            logger.LogError(ex, "The speech worker's answer could not be read");
            Stop("unreadable");
            throw new SpeechException("The voice process gave an unreadable answer.", ex) { Code = "engine" };
        }
        finally
        {
            if (!_disposed && Running)
            {
                _idle ??= new Timer(_ => StopWhenIdle());
                _idle.Change(IdleTimeout, Timeout.InfiniteTimeSpan);
            }

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

        // Ending the worker first also ends a request that is still being spoken (it reads the end of the worker's output at once).
        Stop("closing");

        // Then an idle stop or that request finishes before the app goes on closing (and before its logging is gone).
        if (_gate.Wait(TimeSpan.FromSeconds(10)))
        {
            _gate.Release();
        }
        else
        {
            logger.LogWarning("A speech request or an idle stop had not finished 10 seconds after the speech worker was closed");
        }
    }

    /// <summary>Sends one request and reads its answer line (null: the worker ended). A cancel kills the worker, so the read ends at once.</summary>
    private async Task<string?> ExchangeAsync(Process process, SpeechWorkerRequest request, CancellationToken cancellationToken)
    {
        try
        {
            string? answer;

            // The read below ignores the token once it waits (a process's output is a synchronous pipe underneath): killing the worker
            // ends it with the end of the stream.
            using (cancellationToken.Register(() => KillForCancel(process)))
            {
                await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(request, SpeechWorkerJsonContext.Default.SpeechWorkerRequest).AsMemory(),
                    cancellationToken).ConfigureAwait(false);
                await process.StandardInput.FlushAsync(cancellationToken).ConfigureAwait(false);
                answer = await process.StandardOutput.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            }

            // A kill gives the end of the stream (null) or a broken pipe: that is the cancel.
            cancellationToken.ThrowIfCancellationRequested();
            return answer;
        }
        catch (OperationCanceledException)
        {
            Cancelled(request);
            throw;
        }
        catch (IOException) when (cancellationToken.IsCancellationRequested)
        {
            Cancelled(request);
            throw new OperationCanceledException(cancellationToken);
        }
        catch (IOException ex)
        {
            // The worker is gone (its input pipe broke): handled like an ended worker.
            logger.LogWarning(ex, "The speech worker's pipe broke");
            return null;
        }
        catch (Exception ex) when (_disposed && ex is ObjectDisposedException or InvalidOperationException)
        {
            // The app closed the worker (Dispose) while this request used it.
            logger.LogWarning(ex, "The speech worker was closed by the app while a scene was spoken");
            throw new SpeechException("The voice process was ended because the app is closing.", ex) { Code = "engine" };
        }
    }

    /// <summary>The exit code of a worker whose output ended ("?" if it does not exit within 5 seconds); its error output is read to the end.</summary>
    private string ExitCodeOnceEnded(Process process)
    {
        try
        {
            if (!process.WaitForExit(5000))
            {
                return "?";
            }

            // Only after the exit is confirmed: the call without a timeout also waits until the error output has been read to its end.
            process.WaitForExit();
            return process.ExitCode.ToString(CultureInfo.InvariantCulture);
        }
        catch (InvalidOperationException ex) when (_disposed)
        {
            logger.LogWarning(ex, "The exit code of the speech worker could not be read: the app had already closed it");
            return "?";
        }
    }

    private void KillForCancel(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
            logger.LogInformation("The speech request was cancelled; the speech worker is killed");
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or NotSupportedException)
        {
            // InvalidOperationException: it had already ended (or was closed); the read ends anyway.
            logger.LogInformation(ex, "The speech worker could not be killed after a cancel (it may have ended already)");
        }
    }

    /// <summary>Ends the worker after a cancel and removes the half-written sound file of the request.</summary>
    private void Cancelled(SpeechWorkerRequest request)
    {
        Stop("cancelled");
        try
        {
            if (request.OutputWav.Length > 0 && File.Exists(request.OutputWav))
            {
                File.Delete(request.OutputWav);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "The sound file of a cancelled speech request could not be removed ({File})", Path.GetFileName(request.OutputWav));
        }
    }

    private Process Start(string libraryFolder, string modelFolder, int threads)
    {
        var key = string.Join('|', libraryFolder, modelFolder, threads.ToString(CultureInfo.InvariantCulture));
        lock (_sync)
        {
            if (_process is { HasExited: false } running && key == _startedFor)
            {
                return running;
            }
        }

        Stop("restart");
        if (string.IsNullOrEmpty(ProgramPath))
        {
            throw new SpeechException("The app's own program path is unknown; the voice process cannot start.") { Code = "engine" };
        }

        var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        var info = new ProcessStartInfo(ProgramPath)
        {
            WorkingDirectory = modelFolder,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardInputEncoding = utf8,
            StandardOutputEncoding = utf8,
            StandardErrorEncoding = utf8,
        };
        info.ArgumentList.Add(SpeechWorker.Argument);
        info.ArgumentList.Add(libraryFolder);
        info.ArgumentList.Add(threads.ToString(CultureInfo.InvariantCulture));
        if (LogDirectory.Length > 0)
        {
            info.Environment[SpeechWorker.LogDirectoryVariable] = LogDirectory;
        }
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
            logger.LogError(ex, "The speech worker could not be started");
            throw new SpeechException("The voice process could not be started: " + ex.Message, ex) { Code = "engine" };
        }

        var adoption = children.Adopt(process);
        try
        {
            process.PriorityClass = ProcessPriorityClass.BelowNormal;
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or PlatformNotSupportedException)
        {
            logger.LogWarning(ex, "The priority of the speech worker could not be lowered; it runs at normal priority");
        }

        process.BeginErrorReadLine();
        lock (_sync)
        {
            _process = process;
            _adoption = adoption;
            _startedFor = key;
        }

        logger.LogInformation("Speech worker started (pid {Pid}, {Threads} threads)", process.Id, threads);
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

    /// <summary>
    /// Ends the worker: its input is closed (it exits by itself); after 5 seconds it is killed. Once it has exited its error output is read to
    /// the end. Safe from two threads (closing and an idle stop): only one of them takes the worker.
    /// </summary>
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
            _startedFor = string.Empty;
        }

        if (process is null)
        {
            return;
        }

        try
        {
            var exited = process.HasExited;
            if (!exited)
            {
                try
                {
                    process.StandardInput.Close();
                }
                catch (IOException ex)
                {
                    // A worker that is just ending (killed after a cancel) has already closed its end of the pipe.
                    logger.LogInformation(ex, "The speech worker's input could not be closed ({Reason}); it is ending already", reason);
                }

                exited = process.WaitForExit(5000);
                if (!exited)
                {
                    logger.LogWarning("The speech worker did not end within 5 seconds ({Reason}); it is killed", reason);
                    process.Kill(entireProcessTree: true);
                    exited = process.WaitForExit(5000);
                }
            }

            if (exited)
            {
                // Only after the exit is confirmed: waits until the error output has been read to its end.
                process.WaitForExit();
                logger.LogInformation("Speech worker ended ({Reason})", reason);
            }
            else
            {
                logger.LogWarning("The speech worker had not ended 5 seconds after it was killed ({Reason})", reason);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or IOException)
        {
            logger.LogWarning(ex, "The speech worker could not be ended cleanly ({Reason})", reason);
        }
        finally
        {
            adoption?.Dispose();
            process.Dispose();
        }
    }

    private void Error(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return;
        }

        // The worker writes only failures there (sherpa-onnx runs without its debug output), so every line reaches the log, also one that
        // arrives after the request has already returned.
        logger.LogWarning("speech worker: {Line}", line);
        lock (_errors)
        {
            _errors.Enqueue(line);
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
