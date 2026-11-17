using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using TMConcept.Core.AppOllama;
using TMConcept.Engines.Processes;

namespace TMConcept.Engines.AppOllama;

/// <summary>
/// Starts <c>ollama serve</c> of the app's own copy: no window, its output read (the last lines are kept for an error), its environment
/// built by <see cref="AppOllamaEnvironment.Apply"/> (every inherited <c>OLLAMA_*</c> variable removed first), and tied to the app at once
/// through the child process guard (Windows job object; on Linux ended on exit), so neither the server nor its model runners outlive the
/// app. A program that cannot be started (blocked by an antivirus, not executable, half deleted) is an <see cref="AppOllamaException"/>
/// ("start") with the system's reason. A port counts as free only when it can be bound on 127.0.0.1 and on the
/// wildcard addresses (Windows would let 127.0.0.1 bind beside another program's listener on <c>0.0.0.0</c>). What a crashed server left
/// (its model runners) is ended through the guard's orphan sweep, limited to the program's own folder.
/// </summary>
public sealed class AppOllamaLauncher(ChildProcessGuardBase children, ILogger<AppOllamaLauncher> logger) : IAppOllamaLauncher
{
    /// <inheritdoc />
    /// <remarks>
    /// The port is bound (never listened on: no firewall question) on 127.0.0.1, on 0.0.0.0 and on [::], each with exclusive use on Windows.
    /// Windows lets 127.0.0.1:port bind beside another program's listener on every address (an Ollama the user exposed with
    /// <c>OLLAMA_HOST=0.0.0.0</c>, any service), so the wildcard addresses are tried too: a port counts as free only when none is taken.
    /// </remarks>
    public bool IsPortFree(int port) =>
        CanBind(AddressFamily.InterNetwork, IPAddress.Loopback, port) && CanBind(AddressFamily.InterNetwork, IPAddress.Any, port)
        && (!Socket.OSSupportsIPv6 || CanBind(AddressFamily.InterNetworkV6, IPAddress.IPv6Any, port));

    /// <inheritdoc />
    public int AnyFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }

    /// <inheritdoc />
    public IAppOllamaProcess Start(AppOllamaLaunch launch)
    {
        ArgumentNullException.ThrowIfNull(launch);
        var info = new ProcessStartInfo(launch.Executable)
        {
            WorkingDirectory = launch.WorkingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = false,
        };
        info.ArgumentList.Add("serve");
        AppOllamaEnvironment.Apply(info.Environment, launch.Environment);
        var process = new Process { StartInfo = info, EnableRaisingEvents = true };
        var output = new AppOllamaProcess(process, logger);
        bool started;
        try
        {
            started = process.Start();
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException)
        {
            output.Dispose();
            logger.LogError(ex, "{Server} could not be started", AppOllamaDefaults.Name);
            throw new AppOllamaException($"{AppOllamaDefaults.Name} could not be started: {ex.Message}", ex) { Code = "start" };
        }

        if (!started)
        {
            output.Dispose();
            throw new AppOllamaException($"{AppOllamaDefaults.Name} could not be started.") { Code = "start" };
        }

        output.Attach(children.Adopt(process));
        return output;
    }

    /// <inheritdoc />
    public int EndLeftovers(string programFolder)
    {
        ArgumentException.ThrowIfNullOrEmpty(programFolder);
        if (!Directory.Exists(programFolder))
        {
            return 0;
        }

        var ended = children.SweepOrphans(programFolder);
        if (ended > 0)
        {
            logger.LogWarning("{Count} process(es) left by an ended {Server} were ended", ended, AppOllamaDefaults.Name);
        }

        return ended;
    }

    private static bool CanBind(AddressFamily family, IPAddress address, int port)
    {
        try
        {
            using var socket = new Socket(family, SocketType.Stream, ProtocolType.Tcp);
            if (OperatingSystem.IsWindows())
            {
                socket.ExclusiveAddressUse = true;
            }

            socket.Bind(new IPEndPoint(address, port));
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }
}
