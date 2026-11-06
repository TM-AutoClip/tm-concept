using Avalonia;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TMConcept.Desktop.Composition;
using TMConcept.Desktop.Instance;
using TMConcept.Desktop.Logging;
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
            return RunSpeechWorker(args);
        }

        var options = StartOptions.Parse(args);
        var paths = new AppPaths(options.DataDirectory, DesktopServices.CurrentOs);
        using var instance = SingleInstance.Acquire(paths.DataDirectory);
        if (!instance.IsFirst)
        {
            SignalFirst(instance, paths);
            return 0;
        }

        using var services = DesktopServices.Build(paths);
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(Program));
        var children = services.GetRequiredService<Engines.Processes.ChildProcessGuardBase>();

        // Linux has no job object: when the process is ending on an unhandled exception, the children (ffmpeg, Node, Chrome) are ended here
        // or they would outlive the app.
        services.GetRequiredService<CrashLog>().Attach(terminating: () => children.KillAll());
        AppDomain.CurrentDomain.ProcessExit += (_, _) => children.KillAll();
        logger.LogInformation("TM Concept {Version} starting on {Os} ({Architecture}, {Runtime}); data folder: {Mode}", AppIdentity.Version, AppIdentity.Os,
            AppIdentity.Architecture, AppIdentity.Runtime, options.DataDirectory is null ? "default" : "portable");
        SweepOrphans(children, paths, logger);

        instance.Logger = services.GetRequiredService<ILogger<SingleInstance>>();
        instance.ShowRequested += (_, _) =>
        {
            logger.LogInformation("A second start asked for the window; showing it");
            services.GetRequiredService<Startup.AppStartup>().ShowMainWindow();
        };
        App.Services = services;
        var graphicsCard = services.GetRequiredService<UiPreferencesFile>().Read().UseGraphicsCard;
        logger.LogInformation("Drawing with the {Renderer}", graphicsCard ? "graphics card" : "processor");
        try
        {
            var exit = BuildAvaloniaApp(graphicsCard).StartWithClassicDesktopLifetime(args);
            logger.LogInformation("TM Concept stopped (exit code {Exit})", exit);
            return exit;
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex, "The app stopped with an unhandled exception");
            throw;
        }
    }

    /// <summary>
    /// The speech worker (started by the app itself): no window, no data folder lock, no services. It writes its own log file
    /// (<c>speech-worker-yyyy-MM-dd.log</c>) in the folder the app passes in <see cref="Speech.Speech.SpeechWorker.LogDirectoryVariable"/>.
    /// </summary>
    private static int RunSpeechWorker(string[] args)
    {
        var folder = Environment.GetEnvironmentVariable(Speech.Speech.SpeechWorker.LogDirectoryVariable);
        using var provider = string.IsNullOrEmpty(folder) ? null : new FileLoggerProvider(folder, LogRules.SpeechWorkerKind, TimeProvider.System);
        var logger = provider?.CreateLogger(typeof(Speech.Speech.SpeechWorker).FullName!) ?? NullLogger.Instance;
        new CrashLog(logger).Attach();
        try
        {
            return Speech.Speech.SpeechWorker.Run(args, Console.OpenStandardInput(), Console.OpenStandardOutput(), Console.Error, logger);
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex, "The speech worker stopped with an unhandled exception");
            throw;
        }
    }

    /// <summary>A second start with the same data folder: asks the running app to show its window; one entry in the shared app log says so.</summary>
    private static void SignalFirst(SingleInstance instance, AppPaths paths)
    {
        using var provider = new FileLoggerProvider(paths.LogDirectory, LogRules.AppKind, TimeProvider.System);
        instance.Logger = provider.CreateLogger(typeof(SingleInstance).FullName!);
        instance.Logger.LogInformation("TM Concept {Version} is already running for this data folder; asking it to show its window", AppIdentity.Version);
        instance.SignalFirst();
        instance.Logger = NullLogger.Instance;
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
    public static AppBuilder BuildAvaloniaApp() => BuildAvaloniaApp(graphicsCard: true);

    /// <summary>
    /// Avalonia app builder. Without the graphics card the window is drawn by Skia on the processor: the same picture, but no Direct3D /
    /// OpenGL driver in the process (on Windows its memory and some 40 driver threads; on a Raspberry Pi Mesa and LLVM).
    /// </summary>
    public static AppBuilder BuildAvaloniaApp(bool graphicsCard)
    {
        var builder = AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace();
        return graphicsCard
            ? builder
            : builder.With(new Win32PlatformOptions { RenderingMode = [Win32RenderingMode.Software] })
                .With(new X11PlatformOptions { RenderingMode = [X11RenderingMode.Software] });
    }
}
