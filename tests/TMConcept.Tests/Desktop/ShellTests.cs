using Avalonia.Headless.XUnit;
using Shouldly;
using TMConcept.Core.Activity;
using TMConcept.Core.Notifications;
using TMConcept.Desktop.Logging;
using TMConcept.Desktop.Notifications;
using TMConcept.Desktop.Startup;
using TMConcept.Desktop.ViewModels;
using TMConcept.Desktop.Views;

namespace TMConcept.Tests.Desktop;

public sealed class ShellTests
{
    [AvaloniaFact]
    public async Task MainWindowOpensWithSidebarAndLog()
    {
        using var h = new DesktopHarness();
        await FirstRunTests.CreateAsync(h);
        await h.Get<AppSession>().LoadAsync(CancellationToken.None);
        var shell = h.Get<ShellViewModel>();
        var window = new MainWindow { DataContext = shell };
        window.Show();
        await shell.StartAsync();
        shell.Items.ShouldNotBeEmpty();
        shell.Current.ShouldNotBeNull();
        h.Get<ActivityLog>().Add(ActivityLevel.Warning, "narration off");
        shell.Log.Last().IsWarning.ShouldBeTrue();
        shell.LastLine.ShouldBe("narration off");
        window.Close();
    }

    [AvaloniaFact]
    public async Task NotificationsRespectTheSetting()
    {
        using var h = new DesktopHarness();
        await FirstRunTests.CreateAsync(h);
        var toasts = h.Get<ToastNotifier>();
        toasts.Notify(new AppNotification(NotificationKind.Done, "Video", "ready"));
        toasts.Open.Count.ShouldBe(1);
        toasts.Open[0].Close();

        var session = h.Get<AppSession>();
        session.Settings.NotificationsEnabled = false;
        toasts.Notify(new AppNotification(NotificationKind.Failed, "Video", "failed"));
        toasts.Open.ShouldBeEmpty();
    }

    [AvaloniaFact]
    public async Task SettingsSectionsAreListedInOrder()
    {
        using var h = new DesktopHarness();
        await FirstRunTests.CreateAsync(h);
        var settings = h.Get<SettingsViewModel>();
        settings.Sections.First().ShouldBeOfType<GeneralSettingsViewModel>();
        settings.Sections.Last().ShouldBeOfType<AboutSettingsViewModel>();
        var backup = await settings.ShowAsync<BackupSettingsViewModel>();
        backup.ShouldNotBeNull();
        await backup.BackupNowCommand.ExecuteAsync(null);
        backup.Count.ShouldBe(1);
    }
}
