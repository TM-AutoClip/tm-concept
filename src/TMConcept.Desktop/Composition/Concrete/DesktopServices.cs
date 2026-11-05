using Microsoft.EntityFrameworkCore;
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
        services.AddLogging(b => b.AddProvider(new FileLoggerProvider(paths.LogDirectory)).SetMinimumLevel(LogLevel.Information));
        services.AddSingleton(TimeProvider.System);
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
        services.AddSingleton<Engines.Tools.ToolInstaller>();
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
        services.AddSingleton<IDbContextFactory<ConceptDbContext>, ConceptDbContextFactory>();
        services.AddSingleton<ISettingsStore, SettingsStore>();
        services.AddSingleton<IProfileStore, ProfileStore>();
        services.AddSingleton<IConceptStore, ConceptStore>();
        services.AddSingleton<IRunStore, RunStore>();
        services.AddSingleton<IQueueStore, QueueStore>();
        services.AddSingleton<IVideoStore, VideoStore>();
        services.AddSingleton<IApiKeyStore, ApiKeyStore>();
        services.AddSingleton<IStepModelStore, StepModelStore>();
        services.AddSingleton<IPriceOverrideStore, PriceOverrideStore>();
        services.AddSingleton<IEmbeddingStore, EmbeddingStore>();
    }

    private static void AddDesktop(IServiceCollection services)
    {
        services.AddSingleton<AppSession>();
        services.AddSingleton<ConceptEvents>();
        services.AddSingleton<Core.Concepts.ConceptGenerator>();
        services.AddSingleton<ShellLauncher>();
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

        services.AddSingleton<Dialogs.IConfirmDialog, Dialogs.ConfirmDialog>();
        AddPage<ConceptsViewModel, ConceptsView>(services);
        AddPage<TrashViewModel, TrashView>(services);
        AddPage<ProfileViewModel, ProfileView>(services);
        AddPage<SettingsViewModel, SettingsView>(services);
        AddSection<GeneralSettingsViewModel, GeneralSettingsView>(services);
        AddSection<KeysSettingsViewModel, KeysSettingsView>(services);
        AddSection<ModelsSettingsViewModel, ModelsSettingsView>(services);
        AddSection<ToolsSettingsViewModel, ToolsSettingsView>(services);
        AddSection<BackupSettingsViewModel, BackupSettingsView>(services);
        AddSection<AboutSettingsViewModel, AboutSettingsView>(services);
    }

    /// <summary>Registers a page of the sidebar and its view.</summary>
    internal static void AddPage<TPage, TView>(IServiceCollection services)
        where TPage : PageViewModelBase
        where TView : Avalonia.Controls.Control, new()
    {
        services.AddSingleton<TPage>();
        services.AddSingleton<PageViewModelBase>(s => s.GetRequiredService<TPage>());
        services.AddSingleton<IViewRegistration, ViewRegistration<TPage, TView>>();
    }

    /// <summary>Registers a settings section (a tab of the settings page) and its view.</summary>
    internal static void AddSection<TSection, TView>(IServiceCollection services)
        where TSection : SettingsSectionBase
        where TView : Avalonia.Controls.Control, new()
    {
        services.AddSingleton<TSection>();
        services.AddSingleton<SettingsSectionBase>(s => s.GetRequiredService<TSection>());
        services.AddSingleton<IViewRegistration, ViewRegistration<TSection, TView>>();
    }
}
