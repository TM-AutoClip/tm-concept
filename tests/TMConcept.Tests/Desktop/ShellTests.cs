using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
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
    public async Task MainWindowOpensWithSidebarAndAOneLineActivityStrip()
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
        window.GetVisualDescendants().OfType<ScrollViewer>().ShouldNotContain(s => s.Name == "LogScroll", "not the old panel: one line, a list only on a click");
        window.GetVisualDescendants().OfType<TextBlock>().ShouldContain(t => t.Name == "LastLine");

        // The newest line is on the strip with its time; a warning is yellow, an error red; the list is newest first.
        h.Get<ActivityLog>().Add(ActivityLevel.Warning, "narration off");
        shell.Last!.IsWarning.ShouldBeTrue();
        h.Get<ActivityLog>().Add(ActivityLevel.Error, "Concept generation stopped · exact 0.14 $");
        shell.LastLine.ShouldEndWith(" · Concept generation stopped · exact 0.14 $");
        shell.Last!.IsError.ShouldBeTrue();
        shell.Lines[0].ShouldBeSameAs(shell.Last);
        shell.Lines[1].Line.ShouldEndWith("narration off");
        window.Close();
    }

    [AvaloniaFact]
    public async Task TheStripKeepsTheLastHundredLinesAndShowsTheRunningWorksStep()
    {
        using var h = new DesktopHarness();
        await FirstRunTests.CreateAsync(h);
        var shell = h.Get<ShellViewModel>();
        var log = h.Get<ActivityLog>();
        for (var i = 0; i < 250; i++)
        {
            log.Add(ActivityLevel.Info, "line " + i);
        }

        shell.Lines.Count.ShouldBe(ShellViewModel.ListMax);
        shell.Lines[0].Line.ShouldEndWith("line 249");
        log.Recent().Count.ShouldBe(ActivityLog.Keep, "the memory keeps 200, the list shows 100");

        // A line that names something shows the name on the screen and the placeholder in the file only.
        log.Add(ActivityLevel.Info, "Profile \"Byzantium\" created.", "Profile \"" + ActivityPlaceholders.Name + "\" created.");
        shell.LastLine.ShouldEndWith("Profile \"Byzantium\" created.");

        var work = h.Get<TMConcept.Desktop.Work.WorkStatus>();
        shell.Spinning.ShouldBeFalse();
        var owner = new object();
        work.Show(owner, "Generating concepts · Claude Sonnet 5.5 is thinking (12 s)");
        shell.CurrentStep.ShouldBe("Generating concepts · Claude Sonnet 5.5 is thinking (12 s)");
        shell.Spinning.ShouldBeTrue();
        work.Clear(owner);
        shell.CurrentStep.ShouldBeEmpty();
        shell.Spinning.ShouldBeFalse();
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
