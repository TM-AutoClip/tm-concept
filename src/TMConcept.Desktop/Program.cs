using System.Reflection;
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
    /// Arguments: <c>--speech-worker …</c> runs the speech worker (started by the app itself, see SpeechWorker). <c>--data-dir &lt;folder&gt;</c> (portable mode: data, tools, videos and backups all under that folder; the environment variable
    /// <c>TMCONCEPT_DATA_DIR</c> does the same). A second start with the same data folder asks the running one to show its window and exits.
    /// </summary>
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Length > 0 && args[0] == Speech.Speech.SpeechWorker.Argument)
        {
            // The speech worker (started by the app itself): no window, no data folder lock, no services.
            return Speech.Speech.SpeechWorker.Run(args, Console.OpenStandardInput(), Console.OpenStandardOutput(), Console.Error);
        }

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
        var children = services.GetRequiredService<Engines.Processes.ChildProcessGuardBase>();
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            logger.LogCritical(e.ExceptionObject as Exception, "Unhandled exception (terminating: {Terminating})", e.IsTerminating);
            if (e.IsTerminating)
            {
                // Linux has no job object: the children (ffmpeg, Node, Chrome) are ended here or they would outlive the app.
                children.KillAll();
            }
        };
        AppDomain.CurrentDomain.ProcessExit += (_, _) => children.KillAll();
        TaskScheduler.UnobservedTaskException += (_, e) => logger.LogError(e.Exception, "Unobserved task exception");
        logger.LogInformation("TM Concept {Version} starting on {Os} ({Architecture})",
            typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
            Environment.OSVersion.Platform, System.Runtime.InteropServices.RuntimeInformation.OSArchitecture);
        SweepOrphans(children, paths, logger);

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

    /// <summary>Ends processes an earlier run left in this app's tools folder (it was killed); a failure is logged, the start goes on.</summary>
    private static void SweepOrphans(Engines.Processes.ChildProcessGuardBase children, AppPaths paths, ILogger logger)
    {
        try
        {
            var ended = children.SweepOrphans(paths.ToolsDirectory);
            if (ended > 0)
            {
                logger.LogWarning("{Count} process(es) left by an earlier run were ended", ended);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or IOException or UnauthorizedAccessException)
        {
            logger.LogError(ex, "Processes left by an earlier run could not be checked");
        }
    }

    /// <summary>Avalonia app builder (also used by the designer).</summary>
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace();
}
