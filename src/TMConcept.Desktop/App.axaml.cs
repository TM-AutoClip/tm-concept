using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using TMConcept.Core.Diagnostics;
using TMConcept.Desktop.Startup;

namespace TMConcept.Desktop;

/// <summary>
/// The Avalonia application. The first window is the splash; the services are built after it is up (<see cref="AppBoot"/>, 0.1.38), then
/// the startup flow (first run, unlock, main window) is in <see cref="AppStartup"/>. Without a boot (headless tests) only the styles load.
/// </summary>
public sealed partial class App : Application
{
    /// <summary>Services of the app (set by <see cref="AppBoot"/> once they are built).</summary>
    public static IServiceProvider? Services { get; set; }

    /// <summary>The start the program prepared (set by <see cref="Program"/>; null in headless tests).</summary>
    public static AppBoot? Boot { get; set; }

    /// <inheritdoc />
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    /// <inheritdoc />
    public override void OnFrameworkInitializationCompleted()
    {
        if (Boot is { } boot && ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            StepTimer.Process.End(StartupSteps.Avalonia);
            boot.Start(desktop);
        }

        base.OnFrameworkInitializationCompleted();
    }
}
