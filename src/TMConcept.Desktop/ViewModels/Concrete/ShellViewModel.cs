using System.Collections.ObjectModel;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using TMConcept.Core.Activity;
using TMConcept.Core.Models;
using TMConcept.Core.Notifications;
using TMConcept.Core.Profiles;
using TMConcept.Desktop.Dialogs;
using TMConcept.Desktop.Localization;
using TMConcept.Desktop.Paths;
using TMConcept.Desktop.Startup;
using TMConcept.Desktop.Work;

namespace TMConcept.Desktop.ViewModels;

/// <summary>
/// The main window (0.1.13: the left sidebar became top tabs, at the developer's request). The app bar holds the spending pill (this month's
/// total and, while paid work runs, what it has spent so far; <see cref="SpendingMeter"/>), the page tabs (<see cref="Tabs"/>), and on the
/// right the profile selector (every page is filtered by it), "Report a problem" and Settings (<see cref="SettingsItem"/>, a page drawn as a
/// separate tab button). Below: the current page and the activity log. The log's header has the toggle, the last line while it is folded,
/// the step of the running work (a concept call, "Suggest topics", a video, an install) with a spinner, Copy and Clear; open, it lists the
/// last <see cref="ListMax"/> lines newest first (time and text; a warning yellow, an error red; lines about money say what was paid).
/// Folded by default since 0.1.13 (at 720 pixels the open log took a quarter of the window); the last choice is remembered
/// (<see cref="UiPreferences.LogOpen"/>).
/// </summary>
public sealed partial class ShellViewModel : ObservableObject
{
    private readonly Localizer _text;
    private readonly AppSession _session;
    private readonly IProfileStore _profiles;
    private readonly INotifier _notifier;
    private readonly ILogger<ShellViewModel> _logger;
    private readonly IReportDialog _report;
    private readonly WorkStatus _work;
    private readonly WindowHost _host;
    private readonly SpendingMeter _spending;
    private readonly IModelCatalog _catalog;
    private readonly UiPreferencesFile _preferences;
    private bool _loadingProfiles;

    /// <summary>Lines the panel shows.</summary>
    public const int ListMax = 100;

    /// <summary>Pages from this order on are not main tabs (Settings is drawn on the right of the app bar).</summary>
    public const int TabOrderMax = 90;

    /// <summary>Builds the tabs from every registered page.</summary>
    public ShellViewModel(IEnumerable<PageViewModelBase> pages, Localizer text, AppSession session, IProfileStore profiles, INotifier notifier,
        IReportDialog report, IActivityFeed feed, WorkStatus work, WindowHost host, SpendingMeter spending, ConceptEvents events, IModelCatalog catalog,
        UiPreferencesFile preferences, ILogger<ShellViewModel> logger)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(feed);
        ArgumentNullException.ThrowIfNull(work);
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(spending);
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(preferences);
        _text = text;
        _spending = spending;
        _catalog = catalog;
        _preferences = preferences;
        LogOpen = preferences.Read().LogOpen;
        _session = session;
        _profiles = profiles;
        _notifier = notifier;
        _logger = logger;
        _report = report;
        _host = host;
        Items = new ObservableCollection<NavItemViewModel>(pages.OrderBy(p => p.Order).Select(p => new NavItemViewModel(this, p)));
        Tabs = new ObservableCollection<NavItemViewModel>(Items.Where(i => i.Page.Order < TabOrderMax));
        SettingsItem = Items.FirstOrDefault(i => i.Page is SettingsViewModel);
        foreach (var item in Items)
        {
            item.Page.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(PageViewModelBase.Working))
                {
                    _session.WorkRunning = AnyWorking;
                    OnPropertyChanged(nameof(CanSwitchProfile));
                    OnPropertyChanged(nameof(AnyWorking));
                    OnPropertyChanged(nameof(Spinning));
                }
            };
        }

        _work = work;
        work.Changed += (_, _) =>
        {
            OnPropertyChanged(nameof(CurrentStep));
            OnPropertyChanged(nameof(Spinning));
        };
        foreach (var entry in feed.Recent().TakeLast(ListMax))
        {
            Show(entry);
        }

        feed.Added += (_, entry) =>
        {
            if (Dispatcher.UIThread.CheckAccess())
            {
                Show(entry);
            }
            else
            {
                Dispatcher.UIThread.Post(() => Show(entry));
            }
        };

        spending.Changed += (_, _) => RefreshSpending();
        // A concept run, an edit, a made or failed video: the month is read again (a running work's own part is shown live meanwhile).
        events.Changed += (_, _) => _ = spending.RefreshAsync();
        RefreshTitles();
        RefreshSpending();
        _text.LanguageChanged += (_, _) =>
        {
            RefreshTitles();
            _ = spending.RefreshAsync();
        };
    }

    /// <summary>Every page, in order (the tabs and Settings).</summary>
    public ObservableCollection<NavItemViewModel> Items { get; }

    /// <summary>The main tabs of the app bar (every page before <see cref="TabOrderMax"/>).</summary>
    public ObservableCollection<NavItemViewModel> Tabs { get; }

    /// <summary>The Settings page, drawn as its own tab button on the right of the app bar (null when not registered).</summary>
    public NavItemViewModel? SettingsItem { get; }

    /// <summary>The spending pill: "This month 1.24 $".</summary>
    [ObservableProperty]
    public partial string SpendingText { get; private set; } = string.Empty;

    /// <summary>What the running paid works have spent so far ("now +0.031 $"); empty when nothing paid runs.</summary>
    [ObservableProperty]
    public partial string SpendingLive { get; private set; } = string.Empty;

    /// <summary>The pill's tooltip: the month's parts and the date of the prices.</summary>
    [ObservableProperty]
    public partial string SpendingTip { get; private set; } = string.Empty;

    /// <summary>Profiles (channels).</summary>
    public ObservableCollection<Profile> Profiles { get; } = [];

    /// <summary>Shown page.</summary>
    [ObservableProperty]
    public partial PageViewModelBase? Current { get; set; }

    /// <summary>The selected profile.</summary>
    [ObservableProperty]
    public partial Profile? SelectedProfile { get; set; }

    /// <summary>Whether the activity log panel is open (folded by default; the last choice is remembered).</summary>
    [ObservableProperty]
    public partial bool LogOpen { get; set; }

    /// <summary>Whether a page runs work (a generation, a video queue).</summary>
    public bool AnyWorking => Items.Any(i => i.Page.Working);

    /// <summary>The panel's list: the last activity lines, newest first.</summary>
    public ObservableCollection<ActivityLineViewModel> Lines { get; } = [];

    /// <summary>The newest activity line (null before the first).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LastLine))]
    public partial ActivityLineViewModel? Last { get; private set; }

    /// <summary>The newest line, in the header while the panel is folded: "14:23 · Concepts ready: 5 · exact 0.14 $", or that nothing was done yet.</summary>
    public string LastLine => Last?.Line ?? _text["strip.empty"];

    /// <summary>The step of the running work (the newest one); empty when nothing runs.</summary>
    public string CurrentStep => _work.Text;

    /// <summary>Whether the panel's spinner turns (a page works or a work shows a step).</summary>
    public bool Spinning => AnyWorking || _work.Active;

    /// <summary>The profile can be switched only while no page works for the selected one.</summary>
    public bool CanSwitchProfile => !AnyWorking;

    /// <summary>Asks every running work to stop (closing the window).</summary>
    public void CancelWork()
    {
        foreach (var item in Items.Where(i => i.Page.Working))
        {
            item.Page.CancelWork();
        }
    }

    /// <summary>Loads the profiles and opens the first page.</summary>
    public async Task StartAsync()
    {
        foreach (var page in Items.Select(i => i.Page).OfType<ProfileViewModel>())
        {
            page.OnProfilesChanged(RefreshProfilesAsync);
        }

        await RefreshProfilesAsync().ConfigureAwait(true);
        await _spending.RefreshAsync().ConfigureAwait(true);
        if (Profiles.Count == 0)
        {
            // First start: the first profile is the last step of the setup.
            await NewProfileAsync().ConfigureAwait(true);
        }
        else if (Items.Count > 0)
        {
            await NavigateAsync(Items[0].Page).ConfigureAwait(true);
        }
    }

    /// <summary>Opens the profile page with a new, empty profile.</summary>
    [RelayCommand]
    public async Task NewProfileAsync()
    {
        if (Items.FirstOrDefault(i => i.Page is ProfileViewModel) is not { } item)
        {
            return;
        }

        var page = (ProfileViewModel)item.Page;
        page.StartNew();
        await NavigateAsync(page).ConfigureAwait(true);
    }

    /// <summary>Reloads the profile list (after a profile was added, edited or deleted) and keeps the session's selection.</summary>
    public async Task RefreshProfilesAsync()
    {
        _loadingProfiles = true;
        try
        {
            var all = await _profiles.ListAsync(CancellationToken.None).ConfigureAwait(true);
            Profiles.Clear();
            foreach (var profile in all)
            {
                Profiles.Add(profile);
            }

            SelectedProfile = Profiles.FirstOrDefault(p => p.Id == _session.Profile?.Id) ?? Profiles.FirstOrDefault();
        }
        finally
        {
            _loadingProfiles = false;
        }

        if (SelectedProfile?.Id != _session.Profile?.Id || (SelectedProfile is not null && !ReferenceEquals(SelectedProfile, _session.Profile)))
        {
            await SelectAsync(SelectedProfile).ConfigureAwait(true);
        }
    }

    /// <summary>Opens a page of the given type (pages link to each other).</summary>
    public async Task<TPage?> NavigateAsync<TPage>()
        where TPage : PageViewModelBase
    {
        if (Items.FirstOrDefault(i => i.Page is TPage) is not { } item)
        {
            return null;
        }

        await NavigateAsync(item.Page).ConfigureAwait(true);
        return (TPage)item.Page;
    }

    /// <summary>Opens a page; a failing reload is shown on the page and logged.</summary>
    public async Task NavigateAsync(PageViewModelBase page)
    {
        ArgumentNullException.ThrowIfNull(page);
        if (Current is { } shown && !ReferenceEquals(shown, page) && !await shown.CanLeaveAsync().ConfigureAwait(true))
        {
            // The shown page keeps unsaved changes the user did not let go.
            return;
        }

        foreach (var item in Items)
        {
            item.IsSelected = item.Page == page;
        }

        if (Current is { } previous && !ReferenceEquals(previous, page))
        {
            previous.Deactivate();
        }

        Current = page;
        await ActivateCurrentAsync().ConfigureAwait(true);
    }

    partial void OnSelectedProfileChanged(Profile? value)
    {
        if (!_loadingProfiles)
        {
            _ = SelectAsync(value);
        }
    }

    private async Task SelectAsync(Profile? profile)
    {
        try
        {
            if (Current is { } shown && profile?.Id != _session.Profile?.Id && !await shown.CanLeaveAsync().ConfigureAwait(true))
            {
                // The shown page keeps unsaved changes the user did not let go: the picker goes back to the profile in use.
                _loadingProfiles = true;
                try
                {
                    SelectedProfile = Profiles.FirstOrDefault(p => p.Id == _session.Profile?.Id);
                }
                finally
                {
                    _loadingProfiles = false;
                }

                return;
            }

            await _session.SelectProfileAsync(profile, CancellationToken.None).ConfigureAwait(true);
            foreach (var item in Items)
            {
                await item.Page.ProfileChangedAsync().ConfigureAwait(true);
            }

            await ActivateCurrentAsync().ConfigureAwait(true);
            await _spending.RefreshAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Profile could not be selected");
            _notifier.Notify(new AppNotification(NotificationKind.Failed, "TM Concept", _text.Format("error.generic", ex.Message)));
        }
    }

    private async Task ActivateCurrentAsync()
    {
        if (Current is not { } page)
        {
            return;
        }

        try
        {
            await page.ActivateAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Page {Page} failed to load", page.GetType().Name);
            page.Status = _text.Format("page.load_failed", ex.Message);
        }
    }

    /// <summary>Opens "Report a problem" (nothing is sent from there by itself).</summary>
    [RelayCommand]
    private Task ReportProblemAsync() => _report.ShowAsync();

    [RelayCommand]
    private void ToggleLog() => LogOpen = !LogOpen;

    partial void OnLogOpenChanged(bool value)
    {
        // Saved at once (not at closing): the next start opens it the same way.
        if (value != _preferences.Read().LogOpen)
        {
            _preferences.Change(p => p.LogOpen = value);
        }
    }

    private void RefreshSpending()
    {
        SpendingText = _text.Format("shell.spending.month", _text.Usd(_spending.MonthUsd));
        SpendingLive = _spending.LiveUsd > 0 ? _text.Format("shell.spending.live", _text.Usd(_spending.LiveUsd)) : string.Empty;
        var month = _spending.Breakdown.Length > 0
            ? _text.Usd(_spending.MonthUsd) + " (" + _spending.Breakdown + ")"
            : _text.Usd(_spending.MonthUsd);
        SpendingTip = _text.Format("shell.spending.tip", month, PriceInfo.Date(_catalog));
    }

    /// <summary>Empties the panel; the log file keeps every line.</summary>
    [RelayCommand]
    private void ClearLog()
    {
        Lines.Clear();
        Last = null;
    }

    /// <summary>Copies the panel's lines, oldest first, to the clipboard.</summary>
    [RelayCommand]
    private async Task CopyLogAsync()
    {
        var text = string.Join(Environment.NewLine,
            Lines.Reverse().Select(l => l.Entry.At.ToString("HH:mm:ss", _text.Culture) + " " + l.Entry.Text));
        if (_host.Active?.Clipboard is { } clipboard)
        {
            await clipboard.SetTextAsync(text).ConfigureAwait(true);
        }
        else
        {
            _logger.LogWarning("No window to copy the activity lines to the clipboard from");
        }
    }

    private void Show(ActivityEntry entry)
    {
        var line = new ActivityLineViewModel(entry, entry.At.ToString("HH:mm", _text.Culture) + " · " + entry.Text);
        Lines.Insert(0, line);
        while (Lines.Count > ListMax)
        {
            Lines.RemoveAt(Lines.Count - 1);
        }

        Last = line;
    }

    private void RefreshTitles()
    {
        foreach (var item in Items)
        {
            item.Title = _text[item.Page.TitleKey];
        }
    }
}
