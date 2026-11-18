using Avalonia;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TMConcept.Desktop.Composition;
using TMConcept.Desktop.Instance;
using TMConcept.Desktop.Logging;
using TMConcept.Desktop.Paths;
using Velopack;

namespace TMConcept.Desktop;

/// <summary>Entry point of the desktop app.</summary>
internal static class Program
{
    /// <summary>
    /// Arguments: <c>--speech-worker …</c> runs the speech worker (started by the app itself, see SpeechWorker). <c>--data-dir &lt;folder&gt;</c> (portable mode: data, tools, videos and backups all under that folder; the environment variable
    /// <c>TMCONCEPT_DATA_DIR</c> does the same). <c>--after-update &lt;process id&gt;</c>: started by the old version after an update; waits
    /// until that process has ended. A second start with the same data folder asks the running one to show its window and exits.
    /// </summary>
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Length > 0 && args[0] == Speech.Speech.SpeechWorker.Argument)
        {
            return RunSpeechWorker(args);
        }

        // The installed edition (Velopack): Setup's and the uninstaller's hooks (--veloapp-install / -updated / -obsolete / -uninstall) run here
        // and end the process before the data folder lock is taken or any service is built. A downloaded update is never applied at start: the
        // apply ends every process running from the install folder (a second start or the speech worker would end a working first instance),
        // so it is applied only from the update window when no work runs. The portable program passes straight through.
        VelopackApp.Build().SetAutoApplyOnStartup(false).SetAppUserModelId(Os.TaskbarIcon.AppId).Run();
        var edition = Updates.VelopackEdition.Current();

        var options = StartOptions.Parse(args);
        var dataDirectory = options.DataDirectory ?? DesktopServices.CurrentOs.DefaultDataDirectory();
        if (edition.Kind == Updates.AppEditionKind.Installed && Updates.EditionGuard.Overlaps(dataDirectory, edition.InstallRoot))
        {
            // An uninstall empties the install folder: the data must never live there (Setup started with --installto at the data folder).
            Os.StartupMessage.Show("TM Concept",
                "TM Concept was installed into its data folder (" + dataDirectory + "). Uninstalling it would delete your data, so it does not start. "
                + "Uninstall it from Windows' Apps list (your data stays) and run Setup again without choosing a folder.");
            return 3;
        }

        if (options.WaitForProcess is { } previous)
        {
            // Started by the old version after an update: it is closing; the data folder is free once it has ended.
            WaitForExit(previous);
        }

        var paths = new AppPaths(options.DataDirectory, DesktopServices.CurrentOs);
        using var instance = AcquireInstance(paths);
        if (!instance.IsFirst)
        {
            return 0;
        }

        using var services = DesktopServices.Build(paths, s => s.AddSingleton<Updates.IAppEdition>(edition));
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(Program));
        var children = services.GetRequiredService<Engines.Processes.ChildProcessGuardBase>();

        // The models the repository's model catalog added (the kept copy, checked again; no network): before anything reads the model lists.
        services.GetRequiredService<Catalog.RemoteCatalogUpdater>().ApplySaved();

        // Linux has no job object: when the process is ending on an unhandled exception, the children (ffmpeg, Node, Chrome) are ended here
        // or they would outlive the app.
        services.GetRequiredService<CrashLog>().Attach(terminating: () => children.KillAll());
        AppDomain.CurrentDomain.ProcessExit += (_, _) => children.KillAll();
        logger.LogInformation("TM Concept {Version} ({Edition} edition) starting on {Os} ({Architecture}, {Runtime}); data folder: {Mode}", AppIdentity.Version,
            edition.Kind == Updates.AppEditionKind.Installed ? "installed" : "portable", AppIdentity.Os, AppIdentity.Architecture, AppIdentity.Runtime,
            options.DataDirectory is null ? "default" : "custom data folder");
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
        int exit;
        try
        {
            exit = BuildAvaloniaApp(graphicsCard).StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            // Written once here, as Critical; not thrown on (the process-wide handler would write the same crash a second time). The
            // children end through ProcessExit, the services are disposed by the using above.
            logger.LogCritical(ex, "The app stopped with an unhandled exception");
            return 1;
        }
        finally
        {
            // Closing: a start from now on (the installed copy Setup opens, a new version) waits for the lock instead of asking this window.
            instance.StopListening();
        }

        FreeGraphicsCard(services, logger);
        logger.LogInformation("TM Concept stopped (exit code {Exit})", exit);
        return exit;
    }

    /// <summary>
    /// At exit: the local model the app left on the graphics card is unloaded (a local server would keep it for its own idle time, LM Studio
    /// for good); at most 10 seconds, a failure is only logged.
    /// </summary>
    private static void FreeGraphicsCard(ServiceProvider services, ILogger logger)
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            services.GetRequiredService<Core.Models.IGpuCoordinator>().FreeAsync(timeout.Token).GetAwaiter().GetResult();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            logger.LogWarning(ex, "The graphics card could not be freed at exit");
        }

        try
        {
            // The app's own Ollama server (0.1.20) never outlives the app.
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            services.GetRequiredService<Core.AppOllama.IAppOllamaServer>().StopAsync(timeout.Token).GetAwaiter().GetResult();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            logger.LogWarning(ex, "The app's own Ollama server could not be stopped at exit");
        }
    }

    /// <summary>
    /// The speech worker (started by the app itself): no window, no data folder lock, no services. It writes its own log file
    /// (<c>speech-worker-yyyy-MM-dd.log</c>) in the folder the app passes in <see cref="Speech.Speech.SpeechWorker.LogDirectoryVariable"/>.
    /// </summary>
    private static int RunSpeechWorker(string[] args)
    {
        var folder = Environment.GetEnvironmentVariable(Speech.Speech.SpeechWorker.LogDirectoryVariable);
        using var provider = string.IsNullOrEmpty(folder)
            ? null
            : new FileLoggerProvider(folder, LogRules.SpeechWorkerKind, TimeProvider.System, LogScrubber.ForLogFolder(folder));
        var logger = provider?.CreateLogger(typeof(Speech.Speech.SpeechWorker).FullName!) ?? NullLogger.Instance;
        new CrashLog(logger).Attach();
        try
        {
            return Speech.Speech.SpeechWorker.Run(args, Console.OpenStandardInput(), Console.OpenStandardOutput(), Console.Error, logger);
        }
        catch (Exception ex)
        {
            // Once, as Critical (see Main); the app sees the worker end with code 1 and reads its error output.
            logger.LogCritical(ex, "The speech worker stopped with an unhandled exception");
            Console.Error.WriteLine("The speech worker stopped: " + ex.GetType().Name + ": " + ex.Message);
            return 1;
        }
    }

    /// <summary>Waits (at most a minute) until the process <paramref name="id"/> has ended; a process that is already gone is fine.</summary>
    private static void WaitForExit(int id)
    {
        try
        {
            using var process = System.Diagnostics.Process.GetProcessById(id);
            process.WaitForExit(TimeSpan.FromMinutes(1));
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // Already ended.
        }
    }

    /// <summary>
    /// The data folder's instance lock. A second start asks the running app to show its window (one entry in the shared app log says so) and
    /// gets a non-first instance back. When nobody answers, the running copy is closing (it started this one: "Install TM Concept" runs
    /// Setup, which opens the installed copy while the portable one still frees the graphics card): the lock is waited for, at most
    /// <see cref="SingleInstance.ClosingWait"/>, so the new copy opens instead of ending unseen.
    /// </summary>
    private static SingleInstance AcquireInstance(AppPaths paths)
    {
        var instance = SingleInstance.Acquire(paths.DataDirectory);
        if (instance.IsFirst)
        {
            return instance;
        }

        using var provider = new FileLoggerProvider(paths.LogDirectory, LogRules.AppKind, TimeProvider.System, LogScrubber.For(paths));
        var logger = provider.CreateLogger(typeof(SingleInstance).FullName!);
        instance.Logger = logger;
        logger.LogInformation("TM Concept {Version} is already running for this data folder; asking it to show its window", AppIdentity.Version);
        var shown = instance.SignalFirst();
        instance.Logger = NullLogger.Instance;
        if (shown)
        {
            return instance;
        }

        instance.Dispose();
        logger.LogInformation("The running copy did not answer (it is closing); waiting for it to end");
        var next = SingleInstance.WaitForClosing(paths.DataDirectory, SingleInstance.ClosingWait);
        if (next.IsFirst)
        {
            logger.LogInformation("The closing copy has ended; this one opens");
        }
        else
        {
            logger.LogWarning("The other copy still holds the data folder after {Seconds} s; this start ends", SingleInstance.ClosingWait.TotalSeconds);
        }

        return next;
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
