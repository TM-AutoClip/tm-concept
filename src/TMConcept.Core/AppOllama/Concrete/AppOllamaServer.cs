using System.Globalization;
using Microsoft.Extensions.Logging;

namespace TMConcept.Core.AppOllama;

/// <summary>
/// <see cref="IAppOllamaServer"/>: one <c>ollama serve</c> of the app's own copy, started on the first use (a model's load, a pull) and kept
/// until the app closes or <see cref="StopAsync"/>. The port is <see cref="AppOllamaDefaults.Port"/> or the next free one
/// (<see cref="AppOllamaPorts"/>; never the user's 11434), the environment is <see cref="AppOllamaEnvironment.For"/> (inherited
/// <c>OLLAMA_*</c> variables removed). After the start it asks <c>/api/version</c> until the server answers (at most
/// <see cref="StartTimeout"/>); a server that ended meanwhile fails at once with its last output lines. A server that ended later (a
/// crash) is started again on the next use; what it left (its model runners, which may still hold graphics memory) is ended first
/// (<see cref="IAppOllamaLauncher.EndLeftovers"/>). A health answer counts only while the started process still runs (another server on the
/// port must never be taken for the app's own). One start or stop at a time.
/// </summary>
public sealed class AppOllamaServer(IAppOllamaRuntime runtime, IAppOllamaLauncher launcher, IAppOllamaApi api, AppOllamaLayout layout, TimeProvider clock,
    ILogger<AppOllamaServer> logger) : IAppOllamaServer, IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IAppOllamaProcess? _process;
    private Uri? _address;

    /// <summary>The longest wait for a just-started server to answer.</summary>
    public TimeSpan StartTimeout { get; init; } = AppOllamaDefaults.StartTimeout;

    /// <summary>The wait between two health checks while it starts.</summary>
    public TimeSpan StartPoll { get; init; } = AppOllamaDefaults.StartPoll;

    /// <inheritdoc />
    public bool Running => _process is { HasExited: false } && _address is not null;

    /// <inheritdoc />
    public Uri? Address => Running ? _address : null;

    /// <inheritdoc />
    public async Task<Uri> StartAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_process is { HasExited: false } && _address is { } running)
            {
                return running;
            }

            if (_process is { } ended)
            {
                logger.LogWarning("{Server} had ended (exit code {Code}); starting it again", AppOllamaDefaults.Name,
                    ended.ExitCode?.ToString(CultureInfo.InvariantCulture) ?? "?");
                Forget(ended: true);
            }

            if (!runtime.Ready)
            {
                throw new AppOllamaException($"{AppOllamaDefaults.Name} is not installed. Install it in Settings → Local AI first.") { Code = "missing" };
            }

            int port;
            try
            {
                port = AppOllamaPorts.Choose(launcher.IsPortFree, launcher.AnyFreePort);
            }
            catch (InvalidOperationException ex)
            {
                throw new AppOllamaException(ex.Message, ex) { Code = "port" };
            }

            Directory.CreateDirectory(layout.ModelsDirectory);
            Directory.CreateDirectory(layout.HomeDirectory);
            var launch = new AppOllamaLaunch(runtime.Executable, Path.GetDirectoryName(runtime.Executable) ?? layout.HomeDirectory, port,
                AppOllamaEnvironment.For(layout, port));
            var root = AppOllamaEnvironment.Root(port);
            logger.LogInformation("Starting {Server} {Version} on 127.0.0.1:{Port}", AppOllamaDefaults.Name, runtime.Version, port);
            var process = launcher.Start(launch);
            _process = process;
            await WaitUntilUpAsync(process, root, cancellationToken).ConfigureAwait(false);
            _address = root;
            logger.LogInformation("{Server} answers on port {Port}", AppOllamaDefaults.Name, port);
            return root;
        }
        catch (Exception ex) when (ex is not AppOllamaException)
        {
            // A cancel or a start failure: no half-started server stays behind.
            StopLocked();
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            StopLocked();
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task EndLeftoversAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_process is { HasExited: true } ended)
            {
                logger.LogWarning("{Server} had ended (exit code {Code}); what it left is ended", AppOllamaDefaults.Name,
                    ended.ExitCode?.ToString(CultureInfo.InvariantCulture) ?? "?");
                Forget(ended: true);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Ends the server (the app closes).</summary>
    public void Dispose()
    {
        StopLocked();
        _gate.Dispose();
    }

    private async Task WaitUntilUpAsync(IAppOllamaProcess process, Uri root, CancellationToken cancellationToken)
    {
        var started = clock.GetTimestamp();
        while (true)
        {
            if (process.HasExited)
            {
                var output = process.RecentOutput.Trim();
                logger.LogError("{Server} ended while starting (exit code {Code})", AppOllamaDefaults.Name,
                    process.ExitCode?.ToString(CultureInfo.InvariantCulture) ?? "?");
                StopLocked();
                throw new AppOllamaException($"{AppOllamaDefaults.Name} ended while starting (exit code {process.ExitCode?.ToString(CultureInfo.InvariantCulture) ?? "?"})."
                    + (output.Length > 0 ? " " + output : string.Empty))
                {
                    Code = "start",
                };
            }

            if (await api.IsUpAsync(root, cancellationToken).ConfigureAwait(false))
            {
                // Only while the started process still runs: a server that could not bind ends, and whatever answers then is not ours.
                if (!process.HasExited)
                {
                    return;
                }

                continue;
            }

            if (clock.GetElapsedTime(started) >= StartTimeout)
            {
                logger.LogError("{Server} did not answer within {Seconds} s; it is ended", AppOllamaDefaults.Name, StartTimeout.TotalSeconds);
                StopLocked();
                throw new AppOllamaException($"{AppOllamaDefaults.Name} did not answer within {StartTimeout.TotalSeconds:0} seconds.") { Code = "start" };
            }

            await Task.Delay(StartPoll, clock, cancellationToken).ConfigureAwait(false);
        }
    }

    private void StopLocked()
    {
        if (_process is not { } process)
        {
            return;
        }

        var ended = process.HasExited;
        if (!ended)
        {
            process.Stop();
            logger.LogInformation("{Server} stopped", AppOllamaDefaults.Name);
        }

        Forget(ended);
    }

    /// <summary>
    /// Lets go of the process; when it had ended by itself (not by <see cref="IAppOllamaProcess.Stop"/>, which ends the whole tree) its model
    /// runners may still run, so they are ended too.
    /// </summary>
    private void Forget(bool ended)
    {
        _process?.Dispose();
        _process = null;
        _address = null;
        if (ended && Path.GetDirectoryName(runtime.Executable) is { Length: > 0 } folder)
        {
            launcher.EndLeftovers(folder);
        }
    }
}
