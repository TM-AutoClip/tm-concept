using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TMConcept.Core.Activity;
using TMConcept.Core.Concepts;
using TMConcept.Core.Duplicates;
using TMConcept.Core.Keys;
using TMConcept.Core.Localization;
using TMConcept.Core.Models;
using TMConcept.Core.Notifications;
using TMConcept.Core.Paths;
using TMConcept.Core.Profiles;
using TMConcept.Core.Runs;
using TMConcept.Core.Settings;
using TMConcept.Core.Templates;
using TMConcept.Core.Videos;
using TMConcept.Desktop.Localization;
using TMConcept.Desktop.Logging;
using TMConcept.Desktop.Notifications;
using TMConcept.Desktop.Os;
using TMConcept.Desktop.Paths;
using TMConcept.Desktop.Startup;
using TMConcept.Desktop.ViewModels;
using TMConcept.Desktop.Views;
using TMConcept.Storage.Backup;
using TMConcept.Storage.Crypto;
using TMConcept.Storage.Database;
using TMConcept.Storage.Keys;
using TMConcept.Storage.Stores;

namespace TMConcept.Desktop.Composition;

/// <summary>The composition root: every service of the app. Lists (pages, settings sections, providers, engines) come from here.</summary>
public static class DesktopServices
{
    private static readonly IOsModule[] OsModules = [new WindowsOsModule(), new LinuxOsModule()];

    /// <summary>The operating system module of this computer.</summary>
    public static IOsModule CurrentOs { get; } =
        OsModules.FirstOrDefault(m => m.IsCurrent) ?? throw new PlatformNotSupportedException("TM Concept runs on Windows and Linux.");

    /// <summary>Builds the service provider.</summary>
    public static ServiceProvider Build(AppPaths paths, Action<IServiceCollection>? overrides = null)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var services = new ServiceCollection();
        // Created (and disposed: the file is closed) by the container: Information and above, one file per day, kept 7 days.
        // Paths are masked with this app's folders: the library (profile and video folder names), a data or documents folder outside home.
        services.AddSingleton(LogScrubber.For(paths));
        services.AddSingleton<ILoggerProvider>(s => new FileLoggerProvider(paths.LogDirectory, LogRules.AppKind, s.GetRequiredService<TimeProvider>(),
            s.GetRequiredService<LogScrubber>()));
        services.AddLogging(b => b.SetMinimumLevel(FileLoggerProvider.MinimumLevel));
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(s => new CrashLog(s.GetRequiredService<ILoggerFactory>().CreateLogger("TMConcept.Crash")));
        services.AddSingleton(paths);
        services.AddSingleton<IAppPaths>(paths);
        services.AddSingleton<UiPreferencesFile>();
        services.AddSingleton<Localizer>();
        services.AddSingleton<ILocalizer>(s => s.GetRequiredService<Localizer>());
        services.AddSingleton<LanguageResources>();
        services.AddSingleton(CurrentOs);
        services.AddSingleton<ITemplateSource, EmbeddedTemplateSource>();

        AddStorage(services);
        ProviderServices.Add(services);
        services.AddSingleton<Core.Processes.IProcessRunner, Engines.Processes.ProcessRunner>();
        services.AddSingleton(Engines.Tools.DownloadTransport.Network);
        services.AddSingleton<Engines.Tools.ToolDownloader>();
        services.AddSingleton<Core.Tools.IArchiveDownloader>(s => s.GetRequiredService<Engines.Tools.ToolDownloader>());
        services.AddSingleton<Engines.Media.FfmpegMuxer>();
        services.AddSingleton<Core.Media.IMediaTools, Engines.Media.FfmpegMedia>();
        services.AddSingleton<Core.Videos.VideoPipeline>();
        services.AddSingleton<Core.Videos.VideoCostEstimator>();
        services.AddSingleton<Speech.Speech.SpeechModelInstaller>();
        services.AddSingleton(s => new Speech.Speech.SpeechWorkerClient(s.GetRequiredService<Core.Processes.IChildProcessGuard>(),
            s.GetRequiredService<ILogger<Speech.Speech.SpeechWorkerClient>>()) { LogDirectory = paths.LogDirectory });
        services.AddSingleton<Core.Speech.ISpeechEngine, Speech.Speech.SupertonicSpeechEngine>();
        services.AddSingleton<Core.Speech.ISpeechEngine, Speech.Speech.KokoroSpeechEngine>();
        services.AddSingleton<Speech.Embeddings.LocalEmbedder>();
        services.AddSingleton<Core.Duplicates.ILocalEmbedder>(s => s.GetRequiredService<Speech.Embeddings.LocalEmbedder>());
        services.AddSingleton<Core.Speech.ISpeechEngine, Providers.Speech.OpenAISpeechEngine>();
        services.AddSingleton<Core.Speech.SpeechService>();
        services.AddSingleton<Core.Images.IImageEngine, Providers.Images.FalImageEngine>();
        services.AddSingleton<Core.Images.IImageEngine, Providers.Images.OpenAIImageEngine>();
        services.AddSingleton<Core.Images.IImageEngine, Providers.Images.ZaiImageEngine>();
        services.AddSingleton<Engines.Images.LocalImageStore>();
        services.AddSingleton<Core.Images.IImageEngine, Engines.Images.SdCppImageEngine>();
        services.AddSingleton<Core.Images.ImageDescriber>();
        services.AddSingleton<Core.Hardware.IGpuInfo, Engines.Hardware.NvidiaGpuInfo>();
        services.AddSingleton<Engines.Tools.ToolInstaller>();
        services.AddSingleton<Core.Tools.IToolLocator>(s => s.GetRequiredService<Engines.Tools.ToolInstaller>());
        services.AddSingleton<Core.Processes.IChildProcessGuard>(s => s.GetRequiredService<Engines.Processes.ChildProcessGuardBase>());
        services.AddSingleton<Engines.Engines.ChromeLocator>();
        services.AddSingleton<Engines.Engines.SampleRender>();
        services.AddSingleton<Core.Engines.IVideoEngine, Engines.Engines.HyperFramesEngine>();
        services.AddSingleton<Core.Engines.IVideoEngine, Engines.Engines.RemotionEngine>();
        services.AddSingleton<Core.Engines.IVideoEngine, Engines.Engines.RevideoEngine>();

        // Everything a chosen model can need on this computer (installed when a process that uses it starts, after one confirmation).
        services.AddSingleton<Core.Components.IComponentSource, Engines.Components.EngineComponentSource>();
        services.AddSingleton<Core.Components.IComponentSource, Speech.Components.VoiceComponentSource>();
        services.AddSingleton<Core.Components.IComponentSource, Speech.Components.EmbeddingComponentSource>();
        services.AddSingleton<Core.Components.ComponentPlanner>();
        services.AddSingleton<Components.ComponentInstallFlow>();
        AddDesktop(services);
        overrides?.Invoke(services);
        services.AddSingleton(s =>
        {
            var locator = new ViewLocator();
            foreach (var registration in s.GetServices<IViewRegistration>())
            {
                registration.Register(locator);
            }

            return locator;
        });
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });
    }

    private static void AddStorage(IServiceCollection services)
    {
        services.AddSingleton<DatabaseKey>();
        services.AddSingleton(KdfParameters.Default);
        services.AddSingleton<UiMasterPassword>();
        services.AddSingleton<IMasterPasswordSource>(s => s.GetRequiredService<UiMasterPassword>());
        services.AddSingleton<IKeyProtector, PasswordKeyProtector>();
        CurrentOs.Register(services);
        services.AddSingleton<Vault>();
        services.AddSingleton<BackupService>();
        services.AddSingleton<ConceptDatabase>();
        services.AddSingleton<ISettingsStore, SettingsStore>();
        services.AddSingleton<IProfileStore, ProfileStore>();
        services.AddSingleton<IConceptStore, ConceptStore>();
        services.AddSingleton<IRunStore, RunStore>();
        services.AddSingleton<IQueueStore, QueueStore>();
        services.AddSingleton<IVideoStore, VideoStore>();
        services.AddSingleton<IApiKeyStore, ApiKeyStore>();
        services.AddSingleton<IStepModelStore, StepModelStore>();
        services.AddSingleton<IEmbeddingStore, EmbeddingStore>();
    }

    private static void AddDesktop(IServiceCollection services)
    {
        services.AddSingleton<AppSession>();
        services.AddSingleton<ConceptEvents>();
        services.AddSingleton<Core.Concepts.ConceptGenerator>();
        services.AddSingleton<ShellLauncher>();
        services.AddSingleton<IShellLauncher>(s => s.GetRequiredService<ShellLauncher>());
        services.AddSingleton<WindowHost>();
        services.AddSingleton<FileDialogs>();
        services.AddSingleton<ActivityLog>();
        services.AddSingleton<IActivityLog>(s => s.GetRequiredService<ActivityLog>());
        services.AddSingleton<ToastNotifier>();
        services.AddSingleton<INotifier>(s => s.GetRequiredService<ToastNotifier>());
        services.AddSingleton<AppStartup>();
        services.AddSingleton<ShellViewModel>();
        services.AddTransient<FirstRunViewModel>();
        services.AddTransient<UnlockViewModel>();

        // One set of model pickers per page that uses models (Generate concepts, Concept → video, Profile).
        services.AddTransient<StepPickerViewModel>();
        services.AddSingleton<Dialogs.IApiKeyDialog, Dialogs.ApiKeyDialog>();

        services.AddSingleton<Dialogs.IConfirmDialog, Dialogs.ConfirmDialog>();
        services.AddSingleton<Dialogs.IInstallDialog, Dialogs.InstallDialog>();
        services.AddSingleton<Reports.SystemReport>();
        services.AddSingleton<Reports.ReportNames>();
        services.AddSingleton<Reports.ProblemReport>();
        services.AddTransient<ReportProblemViewModel>();
        services.AddSingleton<Func<ReportProblemViewModel>>(s => () => s.GetRequiredService<ReportProblemViewModel>());
        services.AddSingleton<Dialogs.IReportDialog, Dialogs.ReportDialog>();
        services.AddSingleton<Engines.Playback.FfmpegPlayback>();
        services.AddSingleton<PlayerViewModel>();
        AddPage<ConceptsViewModel, ConceptsView>(services);
        AddPage<VideoViewModel, VideoView>(services);
        AddPage<ProducedViewModel, ProducedView>(services);
        AddPage<TrashViewModel, TrashView>(services);
        AddPage<ProfileViewModel, ProfileView>(services);
        AddPage<SettingsViewModel, SettingsView>(services);
        // The settings page builds its sections the first time it is shown (less memory at start).
        services.AddSingleton<Func<IEnumerable<SettingsSectionBase>>>(s => () => s.GetServices<SettingsSectionBase>());
        AddSection<GeneralSettingsViewModel, GeneralSettingsView>(services);
        AddSection<KeysSettingsViewModel, KeysSettingsView>(services);
        AddSection<BackupSettingsViewModel, BackupSettingsView>(services);
        AddSection<AboutSettingsViewModel, AboutSettingsView>(services);
    }

    /// <summary>Registers a page of the sidebar and its view.</summary>
    internal static void AddPage<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TPage, TView>(IServiceCollection services)
        where TPage : PageViewModelBase
        where TView : Avalonia.Controls.Control, new()
    {
        services.AddSingleton<TPage>();
        services.AddSingleton<PageViewModelBase>(s => s.GetRequiredService<TPage>());
        services.AddSingleton<IViewRegistration, ViewRegistration<TPage, TView>>();
    }

    /// <summary>Registers a settings section (a tab of the settings page) and its view.</summary>
    internal static void AddSection<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TSection, TView>(
        IServiceCollection services)
        where TSection : SettingsSectionBase
        where TView : Avalonia.Controls.Control, new()
    {
        services.AddSingleton<TSection>();
        services.AddSingleton<SettingsSectionBase>(s => s.GetRequiredService<TSection>());
        services.AddSingleton<IViewRegistration, ViewRegistration<TSection, TView>>();
    }
}
