using Avalonia;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TMConcept.Desktop.Composition;
using TMConcept.Desktop.Instance;
using TMConcept.Desktop.Paths;

namespace TMConcept.Desktop;

/// <summary>Entry point of the desktop app.</summary>
internal static class Program
{
    /// <summary>
    /// Arguments: <c>--data-dir &lt;folder&gt;</c> (portable mode: data, tools, videos and backups all under that folder; the environment variable
    /// <c>TMCONCEPT_DATA_DIR</c> does the same). A second start with the same data folder asks the running one to show its window and exits.
    /// </summary>
    [STAThread]
    public static int Main(string[] args)
    {
        var options = StartOptions.Parse(args);
        var paths = new AppPaths(options.DataDirectory, DesktopServices.CurrentOs);
        using var instance = SingleInstance.Acquire(paths.DataDirectory);
        if (!instance.IsFirst)
        {
            instance.SignalFirst();
            return 0;
        }

        using var services = DesktopServices.Build(paths);
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(Program));
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            logger.LogCritical(e.ExceptionObject as Exception, "Unhandled exception (terminating: {Terminating})", e.IsTerminating);
        TaskScheduler.UnobservedTaskException += (_, e) => logger.LogError(e.Exception, "Unobserved task exception");
        logger.LogInformation("TM Concept {Version} starting on {Os} ({Architecture})", typeof(Program).Assembly.GetName().Version,
            Environment.OSVersion.Platform, System.Runtime.InteropServices.RuntimeInformation.OSArchitecture);

        instance.ShowRequested += (_, _) => services.GetRequiredService<Startup.AppStartup>().ShowMainWindow();
        App.Services = services;
        try
        {
            return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex, "The app stopped with an unhandled exception");
            throw;
        }
    }

    /// <summary>Avalonia app builder (also used by the designer).</summary>
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace();
}
