using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TMConcept.Desktop.Instance;

namespace TMConcept.Desktop.Startup;

/// <summary>
/// <see cref="IStartupExit"/> of the running app. <paramref name="services"/> gives the services once they are built (null before: no child
/// process was started yet and the log file is not open).
/// </summary>
public sealed class StartupExit(Func<IServiceProvider?> services, SingleInstance instance) : IStartupExit
{
    /// <inheritdoc />
    public void KillChildren()
    {
        var provider = services();
        var logger = provider?.GetService<ILogger<StartupExit>>();
        // A warning is written to the disk at once (an information line would wait for the next flush, which an ending process never reaches).
        logger?.LogWarning("The start was canceled on the splash window; the app ends");
        try
        {
            provider?.GetService<Engines.Processes.ChildProcessGuardBase>()?.KillAll();
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or ObjectDisposedException)
        {
            logger?.LogWarning(ex, "Child processes could not all be ended while canceling the start");
        }
    }

    /// <inheritdoc />
    public void ReleaseLock()
    {
        instance.StopListening();
        instance.Dispose();
    }

    /// <inheritdoc />
    public void End(int exitCode) => Environment.Exit(exitCode);
}
