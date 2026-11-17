using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using TMConcept.Core.AppOllama;
using TMConcept.Core.Processes;

namespace TMConcept.Engines.AppOllama;

/// <summary>
/// Starts <c>ollama serve</c> of the app's own copy: no window, its output read (the last lines are kept for an error), its environment
/// built by <see cref="AppOllamaEnvironment.Apply"/> (every inherited <c>OLLAMA_*</c> variable removed first), and tied to the app at once
/// through <see cref="IChildProcessGuard"/> (Windows job object; on Linux ended on exit), so neither the server nor its model runners
/// outlive the app.
/// </summary>
public sealed class AppOllamaLauncher(IChildProcessGuard children, ILogger<AppOllamaLauncher> logger) : IAppOllamaLauncher
{
    /// <inheritdoc />
    public bool IsPortFree(int port)
    {
        try
        {
            var listener = new TcpListener(IPAddress.Loopback, port);
            listener.Start();
            listener.Stop();
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }

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
        if (!process.Start())
        {
            output.Dispose();
            throw new InvalidOperationException("The app's Ollama server could not be started.");
        }

        output.Attach(children.Adopt(process));
        return output;
    }
}
