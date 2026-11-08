using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using TMConcept.Core.Activity;
using TMConcept.Core.Notifications;
using TMConcept.Core.Profiles;
using TMConcept.Desktop.Dialogs;
using TMConcept.Desktop.Localization;
using TMConcept.Desktop.Startup;
using TMConcept.Desktop.Work;

namespace TMConcept.Desktop.ViewModels;

/// <summary>
/// The main window: profile selector (every page is filtered by it), the sidebar of pages with "Report a problem" at its bottom, the
/// current page and a thin strip at the bottom: on the left the last activity line (time and text; a warning yellow, an error red; lines
/// about money say what was paid), a click opens the last <see cref="ListMax"/> lines in a small list that is closed by default; on the
/// right the step of the running work (a concept call, "Suggest topics", a video, an install) with a spinner. The developer asked for it
/// on 8 Nov 2026 ("what was done must be shown somewhere, at worst at the bottom of the screen"); it is not the old activity panel.
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
    private bool _loadingProfiles;

    /// <summary>Lines the strip's list shows.</summary>
    public const int ListMax = 100;

    /// <summary>Builds the sidebar from every registered page.</summary>
    public ShellViewModel(IEnumerable<PageViewModelBase> pages, Localizer text, AppSession session, IProfileStore profiles, INotifier notifier,
        IReportDialog report, IActivityFeed feed, WorkStatus work, ILogger<ShellViewModel> logger)
    {
        ArgumentNullException.ThrowIfNull(feed);
        ArgumentNullException.ThrowIfNull(work);
        ArgumentNullException.ThrowIfNull(text);
        _text = text;
        _session = session;
        _profiles = profiles;
        _notifier = notifier;
        _logger = logger;
        _report = report;
        Items = new ObservableCollection<NavItemViewModel>(pages.OrderBy(p => p.Order).Select(p => new NavItemViewModel(this, p)));
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

        RefreshTitles();
        _text.LanguageChanged += (_, _) => RefreshTitles();
    }

    /// <summary>Sidebar.</summary>
    public ObservableCollection<NavItemViewModel> Items { get; }

    /// <summary>Profiles (channels).</summary>
    public ObservableCollection<Profile> Profiles { get; } = [];

    /// <summary>Shown page.</summary>
    [ObservableProperty]
    public partial PageViewModelBase? Current { get; set; }

    /// <summary>The selected profile.</summary>
    [ObservableProperty]
    public partial Profile? SelectedProfile { get; set; }

    /// <summary>Whether a page runs work (a generation, a video queue).</summary>
    public bool AnyWorking => Items.Any(i => i.Page.Working);

    /// <summary>The strip's list: the last activity lines, newest first.</summary>
    public ObservableCollection<ActivityLineViewModel> Lines { get; } = [];

    /// <summary>The newest activity line (null before the first).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LastLine))]
    public partial ActivityLineViewModel? Last { get; private set; }

    /// <summary>The strip's left side: "14:23 · Concepts ready: 5 · exact 0.14 $", or that nothing was done yet.</summary>
    public string LastLine => Last?.Line ?? _text["strip.empty"];

    /// <summary>The step of the running work (the newest one); empty when nothing runs.</summary>
    public string CurrentStep => _work.Text;

    /// <summary>Whether the strip's spinner turns (a page works or a work shows a step).</summary>
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
            await _session.SelectProfileAsync(profile, CancellationToken.None).ConfigureAwait(true);
            foreach (var item in Items)
            {
                await item.Page.ProfileChangedAsync().ConfigureAwait(true);
            }

            await ActivateCurrentAsync().ConfigureAwait(true);
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
