using System.Collections.ObjectModel;
using Avalonia.Input.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using TMConcept.Core.Activity;
using TMConcept.Core.Profiles;
using TMConcept.Desktop.Localization;
using TMConcept.Desktop.Logging;
using TMConcept.Desktop.Startup;

namespace TMConcept.Desktop.ViewModels;

/// <summary>
/// The main window: profile selector (every page is filtered by it), the sidebar of pages, the current page and the activity log panel at
/// the bottom.
/// </summary>
public sealed partial class ShellViewModel : ObservableObject
{
    private readonly Localizer _text;
    private readonly AppSession _session;
    private readonly IProfileStore _profiles;
    private readonly ActivityLog _log;
    private readonly WindowHost _host;
    private readonly ILogger<ShellViewModel> _logger;
    private bool _loadingProfiles;

    /// <summary>Builds the sidebar from every registered page.</summary>
    public ShellViewModel(IEnumerable<PageViewModelBase> pages, Localizer text, AppSession session, IProfileStore profiles, ActivityLog log,
        WindowHost host, ILogger<ShellViewModel> logger)
    {
        ArgumentNullException.ThrowIfNull(text);
        _text = text;
        _session = session;
        _profiles = profiles;
        _log = log;
        _host = host;
        _logger = logger;
        Items = new ObservableCollection<NavItemViewModel>(pages.OrderBy(p => p.Order).Select(p => new NavItemViewModel(this, p)));
        RefreshTitles();
        _text.LanguageChanged += (_, _) => RefreshTitles();
    }

    /// <summary>Sidebar.</summary>
    public ObservableCollection<NavItemViewModel> Items { get; }

    /// <summary>Profiles (channels).</summary>
    public ObservableCollection<Profile> Profiles { get; } = [];

    /// <summary>Activity log lines.</summary>
    public ObservableCollection<ActivityEntry> Log => _log.Entries;

    /// <summary>Shown page.</summary>
    [ObservableProperty]
    public partial PageViewModelBase? Current { get; set; }

    /// <summary>The selected profile.</summary>
    [ObservableProperty]
    public partial Profile? SelectedProfile { get; set; }

    /// <summary>Whether the activity log panel is open.</summary>
    [ObservableProperty]
    public partial bool LogOpen { get; set; } = true;

    /// <summary>The newest log line (shown when the panel is closed).</summary>
    public string LastLine => _log.Entries.Count > 0 ? _log.Entries[^1].Text : string.Empty;

    /// <summary>Loads the profiles and opens the first page.</summary>
    public async Task StartAsync()
    {
        _log.Entries.CollectionChanged += (_, _) => OnPropertyChanged(nameof(LastLine));
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
            _log.Add(ActivityLevel.Error, _text.Format("error.generic", ex.Message));
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

    [RelayCommand]
    private void ToggleLog() => LogOpen = !LogOpen;

    [RelayCommand]
    private void ClearLog() => _log.Clear();

    [RelayCommand]
    private async Task CopyLogAsync()
    {
        var text = string.Join(Environment.NewLine, _log.Entries.Select(e => e.At.ToString("HH:mm:ss", _text.Culture) + " " + e.Level + " " + e.Text));
        if (_host.Active?.Clipboard is { } clipboard)
        {
            await clipboard.SetTextAsync(text).ConfigureAwait(true);
        }
    }

    private void RefreshTitles()
    {
        foreach (var item in Items)
        {
            item.Title = _text[item.Page.TitleKey];
        }
    }
}
