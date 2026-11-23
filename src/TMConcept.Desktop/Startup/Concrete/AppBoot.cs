using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TMConcept.Core.Diagnostics;
using TMConcept.Desktop.Composition;
using TMConcept.Desktop.Instance;
using TMConcept.Desktop.Localization;
using TMConcept.Desktop.Logging;
using TMConcept.Desktop.Paths;
using TMConcept.Desktop.ViewModels;
using TMConcept.Desktop.Views;

namespace TMConcept.Desktop.Startup;

/// <summary>
/// The start before the services exist (0.1.38, the developer: "a program shows a splash with its logo at once when it is double-clicked").
/// <c>Main</c> keeps only the cheap work (Velopack, the options, the install folder check, the instance lock, the interface preferences file)
/// and starts Avalonia; the first window is the splash (<see cref="SplashWindow"/>). Building the services, the kept model catalog and the
/// sweep of processes an earlier run left then run on a pool thread, each named on the splash; <see cref="AppStartup"/> takes over with the
/// vault, the database and the session. Its Cancel ends the child processes, frees the lock and ends the process at any step.
/// </summary>
public sealed class AppBoot
{
    private readonly AppPaths _paths;
    private readonly StartOptions _options;
    private readonly Updates.IAppEdition _edition;
    private readonly SingleInstance _instance;
    private readonly IStepTimer _timer = StepTimer.Process;
    private readonly Task<Localizer> _texts;
    private readonly bool _graphicsCard;
    private SplashWindow? _splash;
    private AppStartup? _startup;

    /// <summary>
    /// Begins loading the splash window's texts in the preferred language (none chosen yet = the system's) on a pool thread, while Avalonia
    /// starts.
    /// </summary>
    public AppBoot(AppPaths paths, StartOptions options, Updates.IAppEdition edition, SingleInstance instance, UiPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        var language = preferences.Language;
        _graphicsCard = preferences.UseGraphicsCard;
        _paths = paths;
        _options = options;
        _edition = edition;
        _instance = instance;
        _texts = Task.Run(() =>
        {
            var texts = new Localizer();
            texts.Use(language.Length > 0 ? language : Localizer.SystemLanguage());
            return texts;
        });
    }

    /// <summary>The services, once the first start step has built them (the program disposes them at exit).</summary>
    public ServiceProvider? Services { get; private set; }

    /// <summary>Shows the splash window and runs the start (on the interface thread, from the application's initialization).</summary>
    public void Start(IClassicDesktopStyleApplicationLifetime desktop)
    {
        ArgumentNullException.ThrowIfNull(desktop);
        // Closing the splash (or the first-run / unlock window that follows it) must not end the app: it ends on Shutdown only.
        desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        _ = StartAsync(desktop);
    }

    /// <summary>A second start asked for the window: the splash while the start runs, else the main (or first-run / unlock) window.</summary>
    public void ShowRequested() => Dispatcher.UIThread.Post(() =>
    {
        if (_startup is { } startup)
        {
            startup.ShowMainWindow();
            return;
        }

        if (_splash is { } splash)
        {
            splash.Activate();
        }
    });

    private async Task StartAsync(IClassicDesktopStyleApplicationLifetime desktop)
    {
        StartupSequence sequence;
        SplashViewModel model;
        SplashWindow splash;
        try
        {
            // Loaded while Avalonia started; normally done already.
            var texts = await _texts.ConfigureAwait(true);
            sequence = new StartupSequence(new StartupExit(() => Services, _instance));
            model = new SplashViewModel(texts, sequence);
            splash = new SplashWindow { DataContext = model };
            Os.TaskbarIcon.ApplyWhenOpen(splash, NullLogger.Instance);
            _splash = splash;
            desktop.MainWindow = splash;
            splash.Show();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // No window could open (the texts, the splash): the app ends on Shutdown only, so without this it would stay running unseen,
            // holding the data folder's lock. The log file is not open yet (no services): the reason goes to the error output.
            Console.Error.WriteLine("TM Concept could not show its start window: " + ex.GetType().Name + ": " + ex.Message);
            desktop.Shutdown(1);
            return;
        }

        _timer.Mark("startup.at.splash");

        try
        {
            var outcome = await sequence.RunAsync(
            [
                new StartupStep(StartupStage.Preparing, _ => BuildServicesAsync()),
                new StartupStep(StartupStage.Components, _ => ComponentsAsync()),
            ], model).ConfigureAwait(true);
            if (outcome != StartupOutcome.Completed)
            {
                // The splash shows why (or the process is ending); nothing opens after it.
                return;
            }

            var services = Services!;
            App.Services = services;
            var startup = services.GetRequiredService<AppStartup>();
            using (_timer.Step("startup.app_run"))
            {
                startup.Run(desktop, new StartupSplash(splash, model, sequence));
            }

            _startup = startup;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // The interface part of the start failed (the services are built): written once and shown on the splash, which then closes the app.
            Services?.GetService<ILogger<AppBoot>>()?.LogCritical(ex, "The start failed");
            sequence.Fail(model, model.Stage, ex.Message);
        }
    }

    private Task BuildServicesAsync()
    {
        ServiceProvider services;
        using (_timer.Step("startup.services"))
        {
            services = DesktopServices.Build(_paths, s => s.AddSingleton(_edition));
        }

        Services = services;
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(Program));
        var children = services.GetRequiredService<Engines.Processes.ChildProcessGuardBase>();
        // Linux has no job object: when the process is ending on an unhandled exception, the children (ffmpeg, Node, Chrome) are ended here
        // or they would outlive the app.
        services.GetRequiredService<CrashLog>().Attach(terminating: () => children.KillAll());
        AppDomain.CurrentDomain.ProcessExit += (_, _) => children.KillAll();
        logger.LogInformation("TM Concept {Version} ({Edition} edition) starting on {Os} ({Architecture}, {Runtime}); data folder: {Mode}", AppIdentity.Version,
            _edition.Kind == Updates.AppEditionKind.Installed ? "installed" : "portable", AppIdentity.Os, AppIdentity.Architecture, AppIdentity.Runtime,
            _options.DataDirectory is null ? "default" : "custom data folder");
        logger.LogInformation("Drawing with the {Renderer}", _graphicsCard ? "graphics card" : "processor");
        // The lines measured so far are written after the start line, every later one at once.
        _timer.Attach(services.GetRequiredService<ILoggerFactory>().CreateLogger(StepTimer.Category));
        _instance.Logger = services.GetRequiredService<ILogger<SingleInstance>>();
        return Task.CompletedTask;
    }

    private Task ComponentsAsync()
    {
        var services = Services!;
        // The models the repository's model catalog added (the kept copy, checked again; no network): before anything reads the model lists.
        using (_timer.Step("startup.catalog_saved"))
        {
            services.GetRequiredService<Catalog.RemoteCatalogUpdater>().ApplySaved();
        }

        using (_timer.Step("startup.sweep_orphans"))
        {
            SweepOrphans(services.GetRequiredService<Engines.Processes.ChildProcessGuardBase>(), services.GetRequiredService<ILogger<AppBoot>>());
        }

        return Task.CompletedTask;
    }

    /// <summary>Ends processes an earlier run left in this app's tools folder (it was killed); a failure is logged, the start goes on.</summary>
    private void SweepOrphans(Engines.Processes.ChildProcessGuardBase children, ILogger logger)
    {
        try
        {
            var ended = children.SweepOrphans(_paths.ToolsDirectory);
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
}
