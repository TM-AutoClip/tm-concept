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
/// request, so its memory goes back to the system. One request at a time. A cancelled request ends the worker (the next one starts it
/// again). The worker is tied to the app like every other child process.
/// </summary>
public sealed class SpeechWorkerClient(IChildProcessGuard children, ILogger<SpeechWorkerClient> logger) : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Queue<string> _errors = new();
    private Process? _process;
    private IDisposable? _adoption;
    private string _startedFor = string.Empty;
    private Timer? _idle;
    private bool _disposed;

    /// <summary>How long a worker without requests stays (it holds the voice model in memory).</summary>
    public TimeSpan IdleTimeout { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>The program started as the worker (the app's own executable; tests give their own).</summary>
    public string ProgramPath { get; init; } = Environment.ProcessPath ?? string.Empty;

    /// <summary>Whether a worker runs now.</summary>
    public bool Running => _process is { HasExited: false };

    /// <summary>Speaks one request with the libraries of <paramref name="libraryFolder"/> and the model of <paramref name="modelFolder"/>.</summary>
    public async Task<SpeechWorkerReply> SpeakAsync(string libraryFolder, string modelFolder, int threads, SpeechWorkerRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _idle?.Change(Timeout.Infinite, Timeout.Infinite);
            var process = Start(libraryFolder, modelFolder, threads);
            string? answer;
            try
            {
                await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(request, SpeechWorkerJsonContext.Default.SpeechWorkerRequest).AsMemory(),
                    cancellationToken).ConfigureAwait(false);
                await process.StandardInput.FlushAsync(cancellationToken).ConfigureAwait(false);
                answer = await process.StandardOutput.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                Stop("cancelled");
                throw;
            }
            catch (IOException ex)
            {
                // The worker is gone (its input pipe broke): handled like an ended worker below.
                logger.LogWarning(ex, "The speech worker's pipe broke");
                answer = null;
            }

            if (answer is null)
            {
                var exit = process.WaitForExit(5000) ? process.ExitCode.ToString(CultureInfo.InvariantCulture) : "?";
                var errors = LastErrors();
                logger.LogError("The speech worker ended unexpectedly (exit code {Exit}); its last messages: {Errors}", exit, errors);
                Stop("ended");
                throw new SpeechException($"The voice process ended unexpectedly (exit code {exit}). {errors}".Trim()) { Code = "engine" };
            }

            var reply = JsonSerializer.Deserialize(answer, SpeechWorkerJsonContext.Default.SpeechWorkerReply)
                ?? throw new SpeechException("The voice process gave an empty answer.") { Code = "engine" };
            if (!reply.Ok)
            {
                logger.LogError("The speech worker could not speak ({Code}): {Error}; its last messages: {Errors}", reply.Code, reply.Error, LastErrors());
                if (reply.Code == "model")
                {
                    // The worker stops after a model or library failure.
                    Stop("failed");
                }
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
            if (!_disposed && _process is not null)
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
        Stop("closing");
        _gate.Dispose();
    }

    private Process Start(string libraryFolder, string modelFolder, int threads)
    {
        var key = string.Join('|', libraryFolder, modelFolder, threads.ToString(CultureInfo.InvariantCulture));
        if (_process is { HasExited: false } running && key == _startedFor)
        {
            return running;
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
        var process = new Process { StartInfo = info };
        process.ErrorDataReceived += (_, e) => Error(e.Data);
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

        _adoption = children.Adopt(process);
        try
        {
            process.PriorityClass = ProcessPriorityClass.BelowNormal;
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or PlatformNotSupportedException)
        {
            logger.LogDebug(ex, "The priority of the speech worker could not be lowered");
        }

        process.BeginErrorReadLine();
        _process = process;
        _startedFor = key;
        lock (_errors)
        {
            _errors.Clear();
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

    /// <summary>Ends the worker: its input is closed (it exits by itself); after 5 seconds it is killed.</summary>
    private void Stop(string reason)
    {
        var process = _process;
        if (process is null)
        {
            return;
        }

        _process = null;
        _startedFor = string.Empty;
        try
        {
            if (!process.HasExited)
            {
                process.StandardInput.Close();
                if (!process.WaitForExit(5000))
                {
                    logger.LogWarning("The speech worker did not end within 5 seconds ({Reason}); it is killed", reason);
                    process.Kill(entireProcessTree: true);
                    process.WaitForExit(5000);
                }
            }

            logger.LogInformation("Speech worker ended ({Reason})", reason);
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or IOException)
        {
            logger.LogWarning(ex, "The speech worker could not be ended cleanly ({Reason})", reason);
        }
        finally
        {
            _adoption?.Dispose();
            _adoption = null;
            process.Dispose();
        }
    }

    private void Error(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return;
        }

        logger.LogDebug("speech worker: {Line}", line);
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
