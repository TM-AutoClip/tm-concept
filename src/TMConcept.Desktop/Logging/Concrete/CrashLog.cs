using Microsoft.Extensions.Logging;

namespace TMConcept.Desktop.Logging;

/// <summary>
/// Writes what nothing else caught, as Critical: an unhandled exception of the process (<see cref="AppDomain.UnhandledException"/>), a task
/// exception nobody observed (<see cref="TaskScheduler.UnobservedTaskException"/>) and an exception that reached the interface thread.
/// </summary>
public sealed class CrashLog(ILogger logger)
{
    /// <summary>Hooks the process-wide events; <paramref name="terminating"/> runs after the entry is written when the process is ending.</summary>
    public void Attach(Action? terminating = null)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            Unhandled(e.ExceptionObject, e.IsTerminating);
            if (e.IsTerminating)
            {
                terminating?.Invoke();
            }
        };
        TaskScheduler.UnobservedTaskException += (_, e) => Unobserved(e.Exception);
    }

    /// <summary>An unhandled exception of the process.</summary>
    public void Unhandled(object? exceptionObject, bool terminating)
    {
        if (exceptionObject is Exception exception)
        {
            logger.LogCritical(exception, "Unhandled exception (terminating: {Terminating})", terminating);
        }
        else
        {
            logger.LogCritical("Unhandled non-exception object {Type} (terminating: {Terminating})", exceptionObject?.GetType().FullName ?? "null", terminating);
        }
    }

    /// <summary>A task exception nobody observed.</summary>
    public void Unobserved(Exception exception) => logger.LogCritical(exception, "Unobserved task exception");

    /// <summary>An exception that reached the interface thread (the app shows it and goes on).</summary>
    public void Interface(Exception exception) => logger.LogCritical(exception, "Unhandled exception on the interface thread");
}
