using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;
using Shouldly;
using TMConcept.Core.Activity;
using TMConcept.Core.Models;
using TMConcept.Core.Notifications;
using TMConcept.Core.Runs;
using TMConcept.Desktop.Localization;
using TMConcept.Desktop.Logging;
using TMConcept.Desktop.Notifications;
using TMConcept.Desktop.Paths;
using TMConcept.Desktop.Startup;
using TMConcept.Desktop.ViewModels;
using TMConcept.Desktop.Views;
using TMConcept.Desktop.Work;

namespace TMConcept.Tests.Desktop;

public sealed class ShellTests
{
    [AvaloniaFact]
    public async Task MainWindowOpensAt1280x720WithTopTabsSettingsApartAndAFoldedLog()
    {
        using var h = new DesktopHarness();
        await FirstRunTests.CreateAsync(h);
        await h.Get<AppSession>().LoadAsync(CancellationToken.None);
        await h.Get<TMConcept.Core.Profiles.IProfileStore>().SaveAsync(new TMConcept.Core.Profiles.Profile { Name = "Byzantium", ConceptText = "x" },
            CancellationToken.None);
        var text = h.Get<Localizer>();
        var shell = h.Get<ShellViewModel>();
        var window = new MainWindow { DataContext = shell };
        h.Get<LanguageResources>().Attach(window.Resources.MergedDictionaries);
        window.Show();
        await shell.StartAsync();
        Dispatcher.UIThread.RunJobs();
        (window.Width, window.Height, window.MinWidth, window.MinHeight).ShouldBe((1280, 720, 1024, 600));
        window.ClientSize.ShouldBe(new Size(1280, 720));
        shell.Current.ShouldBeOfType<ConceptsViewModel>("the first tab opens");

        // Five main tabs in order; Settings is a page drawn apart on the right, not a second list.
        shell.Tabs.Select(t => t.Page.GetType()).ShouldBe([typeof(ConceptsViewModel), typeof(VideoViewModel), typeof(ProducedViewModel),
            typeof(TrashViewModel), typeof(ProfileViewModel)]);
        var strip = window.FindControl<ItemsControl>("TabStrip")!;
        var tabs = strip.GetVisualDescendants().OfType<Button>().ToList();
        tabs.Count.ShouldBe(5);
        tabs.Select(b => b.GetVisualDescendants().OfType<TextBlock>().First().Text)
            .ShouldBe(new[] { "nav.concepts", "nav.video", "nav.produced", "nav.trash", "nav.profile" }.Select(k => text[k]));
        tabs[0].Classes.ShouldContain("selected");
        var settings = window.FindControl<Button>("SettingsTab")!;
        settings.IsEffectivelyVisible.ShouldBeTrue();
        settings.Command.ShouldBeSameAs(shell.SettingsItem!.OpenCommand);

        // A tab opens its page and moves the underline; Settings works the same way.
        await ((IAsyncRelayCommand)tabs[1].Command!).ExecuteAsync(null);
        Dispatcher.UIThread.RunJobs();
        shell.Current.ShouldBeOfType<VideoViewModel>();
        tabs[1].Classes.ShouldContain("selected");
        tabs[0].Classes.ShouldNotContain("selected");
        await ((IAsyncRelayCommand)settings.Command!).ExecuteAsync(null);
        Dispatcher.UIThread.RunJobs();
        shell.Current.ShouldBeOfType<SettingsViewModel>();
        settings.Classes.ShouldContain("selected");
        tabs[1].Classes.ShouldNotContain("selected");

        // A page's badge (the video queue count) shows as a small pill after the tab's title.
        h.Get<VideoViewModel>().Badge = "2";
        Dispatcher.UIThread.RunJobs();
        tabs[1].GetVisualDescendants().OfType<TextBlock>().ShouldContain(t => t.Text == "2" && t.IsEffectivelyVisible);
        tabs[0].GetVisualDescendants().OfType<Border>().Where(b => b.Classes.Contains("pill")).ShouldAllBe(b => !b.IsVisible);

        // The spending pill at the top left: nothing paid yet; the tooltip names the prices' date.
        window.FindControl<TextBlock>("SpendingText")!.Text.ShouldBe(text.Format("shell.spending.month", text.Usd(0)));
        window.FindControl<TextBlock>("SpendingLiveText")!.IsVisible.ShouldBeFalse();
        shell.SpendingTip.ShouldBe(text.Format("shell.spending.tip", text.Usd(0), PriceInfo.Date(h.Get<IModelCatalog>())));

        // The app bar is one 44-pixel line; the log starts folded: one line with the newest entry.
        window.FindControl<Border>("AppBar")!.Bounds.Height.ShouldBe(44);
        shell.LogOpen.ShouldBeFalse("the log starts folded");
        window.FindControl<ScrollViewer>("LogScroll")!.IsVisible.ShouldBeFalse();
        window.FindControl<TextBlock>("LastLine")!.IsVisible.ShouldBeTrue();
        h.Get<ActivityLog>().Add(ActivityLevel.Warning, "narration off");
        shell.Last!.IsWarning.ShouldBeTrue();
        h.Get<ActivityLog>().Add(ActivityLevel.Error, "Concept generation stopped · exact 0.14 $");
        shell.LastLine.ShouldEndWith(" · Concept generation stopped · exact 0.14 $");
        shell.Last!.IsError.ShouldBeTrue();
        shell.Lines[0].ShouldBeSameAs(shell.Last);
        shell.Lines[1].Line.ShouldEndWith("narration off");

        // Opening it is remembered for the next start.
        shell.ToggleLogCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        shell.LogOpen.ShouldBeTrue();
        h.Get<UiPreferencesFile>().Read().LogOpen.ShouldBeTrue("the open log is remembered");
        window.FindControl<ScrollViewer>("LogScroll")!.IsVisible.ShouldBeTrue();
        window.FindControl<TextBlock>("LastLine")!.IsVisible.ShouldBeFalse();

        // Clear empties the panel only.
        shell.ClearLogCommand.Execute(null);
        shell.Lines.ShouldBeEmpty();
        shell.Last.ShouldBeNull();
        h.Get<ActivityLog>().Recent().ShouldNotBeEmpty("the memory and the log file keep the lines");
        shell.ToggleLogCommand.Execute(null);
        h.Get<UiPreferencesFile>().Read().LogOpen.ShouldBeFalse();
        window.Close();
    }

    [AvaloniaFact]
    public async Task AtTheMinimumWidthEveryTabFitsBeforeTheProfileSelector()
    {
        foreach (var (width, narrow) in new[] { (1024, true), (1280, false) })
        {
            using var h = new DesktopHarness();
            await FirstRunTests.CreateAsync(h);
            await h.Get<AppSession>().LoadAsync(CancellationToken.None);
            h.Get<Localizer>().Use("tr");
            var shell = h.Get<ShellViewModel>();
            await shell.StartAsync();
            h.Get<VideoViewModel>().Badge = "12";
            h.Get<ProducedViewModel>().Badge = "140";
            h.Get<TrashViewModel>().Badge = "3";
            var window = new MainWindow { DataContext = shell, Width = width, Height = 600 };
            h.Get<LanguageResources>().Attach(window.Resources.MergedDictionaries);
            window.Show();
            Dispatcher.UIThread.RunJobs();
            window.Classes.Contains("narrow").ShouldBe(narrow);
            window.FindControl<TextBlock>("AppTitle")!.IsVisible.ShouldBe(!narrow);
            window.FindControl<ComboBox>("ProfilePicker")!.MaxWidth.ShouldBe(narrow ? 150 : 200);
            var last = window.FindControl<ItemsControl>("TabStrip")!.GetVisualDescendants().OfType<Button>().Last();
            var right = last.TranslatePoint(new Point(last.Bounds.Width, 0), window)!.Value.X;
            var picker = window.FindControl<ComboBox>("ProfilePicker")!;
            right.ShouldBeLessThanOrEqualTo(picker.TranslatePoint(new Point(0, 0), window)!.Value.X, $"the last tab is not cut at {width}");
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task TheSpendingPillShowsTheMonthAndWhatARunningWorkHasSpent()
    {
        using var h = new DesktopHarness();
        await FirstRunTests.CreateAsync(h);
        await h.Get<AppSession>().LoadAsync(CancellationToken.None);
        var text = h.Get<Localizer>();
        var shell = h.Get<ShellViewModel>();
        await shell.StartAsync();
        shell.SpendingText.ShouldBe(text.Format("shell.spending.month", text.Usd(0)));
        shell.SpendingLive.ShouldBeEmpty();

        var meter = h.Get<SpendingMeter>();
        var run = new object();
        meter.Live(run, 0.031m);
        shell.SpendingLive.ShouldBe(text.Format("shell.spending.live", text.Usd(0.031m)));

        // The run is saved, then it ends: the month counts it and the live part goes.
        await h.Get<IRunStore>().AddAsync(new Run { Kind = RunKind.Concepts, Provider = "anthropic", ActualUsd = 0.031m,
            Status = RunStatus.Succeeded }, CancellationToken.None);
        await meter.EndAsync(run);
        shell.SpendingLive.ShouldBeEmpty();
        shell.SpendingText.ShouldBe(text.Format("shell.spending.month", text.Usd(0.031m)));
        shell.SpendingTip.ShouldContain("anthropic " + text.Usd(0.031m));
    }

    [AvaloniaFact]
    public async Task ThePanelKeepsTheLastHundredLinesAndShowsTheRunningWorksStep()
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
