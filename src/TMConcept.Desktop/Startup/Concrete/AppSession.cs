using TMConcept.Core.Profiles;
using TMConcept.Core.Settings;

namespace TMConcept.Desktop.Startup;

/// <summary>
/// State of the running app: settings (loaded once after unlock, saved through here so every reader sees the change) and the selected
/// profile every page is filtered by.
/// </summary>
public sealed class AppSession(ISettingsStore store, IProfileStore profiles)
{
    /// <summary>Current settings (defaults until loaded).</summary>
    public AppSettings Settings { get; internal set; } = new();

    /// <summary>The selected profile; <c>null</c> when there is none yet.</summary>
    public Profile? Profile { get; private set; }

    /// <summary>Whether a page runs work for the selected profile (set by the main window); the profile cannot be switched or deleted then.</summary>
    public bool WorkRunning { get; set; }

    /// <summary>A message to show on the next page that has a status line (for example after a restore).</summary>
    public string? PendingMessage { get; set; }

    /// <summary>Raised after settings were saved.</summary>
    public event EventHandler? Changed;

    /// <summary>Raised after the selected profile changed (or was edited).</summary>
    public event EventHandler? ProfileChanged;

    /// <summary>Loads settings and the selected profile.</summary>
    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        Settings = await store.GetAsync(cancellationToken).ConfigureAwait(true);
        var all = await profiles.ListAsync(cancellationToken).ConfigureAwait(true);
        Profile = all.FirstOrDefault(p => p.Id == Settings.SelectedProfileId) ?? all.FirstOrDefault();
    }

    /// <summary>Saves and notifies.</summary>
    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        await store.SaveAsync(settings, cancellationToken).ConfigureAwait(true);
        Settings = settings;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Selects a profile (or none) and remembers it.</summary>
    public async Task SelectProfileAsync(Profile? profile, CancellationToken cancellationToken)
    {
        Profile = profile;
        Settings.SelectedProfileId = profile?.Id;
        await store.SaveAsync(Settings, cancellationToken).ConfigureAwait(true);
        ProfileChanged?.Invoke(this, EventArgs.Empty);
    }
}
