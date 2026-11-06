using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using TMConcept.Core.Paths;
using TMConcept.Desktop.Localization;
using TMConcept.Desktop.Os;
using TMConcept.Storage.Backup;

namespace TMConcept.Desktop.ViewModels;

/// <summary>
/// Backup settings: what goes into "Back up now" (asked every time: API keys start unticked) and into the automatic backups (daily and before
/// a large change; saved, default everything except the API keys), back up now, change the backup password (the current one is required),
/// open the backup folder, last backup. Backups are password-encrypted (Argon2id + AES-256-GCM); videos are never in them.
/// </summary>
public sealed partial class BackupSettingsViewModel(BackupService backup, IAppPaths paths, ShellLauncher shell, Localizer text,
    ILogger<BackupSettingsViewModel> logger) : SettingsSectionBase
{
    /// <inheritdoc />
    public override string TitleKey => "settings.backup";

    /// <inheritdoc />
    public override int Order => 80;

    /// <summary>Last backup in the current culture.</summary>
    [ObservableProperty]
    public partial string LastBackup { get; set; } = string.Empty;

    /// <summary>Number of backup files.</summary>
    [ObservableProperty]
    public partial int Count { get; set; }

    /// <summary>Back up now: API keys (unticked until the user ticks them).</summary>
    [ObservableProperty]
    public partial bool NowKeys { get; set; }

    /// <summary>Back up now: profiles with their model choices and sample images.</summary>
    [ObservableProperty]
    public partial bool NowProfiles { get; set; } = true;

    /// <summary>Back up now: concepts, the trash, the video list and costs.</summary>
    [ObservableProperty]
    public partial bool NowConcepts { get; set; } = true;

    /// <summary>Back up now: settings.</summary>
    [ObservableProperty]
    public partial bool NowSettings { get; set; } = true;

    /// <summary>Automatic backups: API keys.</summary>
    [ObservableProperty]
    public partial bool AutoKeys { get; set; }

    /// <summary>Automatic backups: profiles.</summary>
    [ObservableProperty]
    public partial bool AutoProfiles { get; set; } = true;

    /// <summary>Automatic backups: concepts.</summary>
    [ObservableProperty]
    public partial bool AutoConcepts { get; set; } = true;

    /// <summary>Automatic backups: settings.</summary>
    [ObservableProperty]
    public partial bool AutoSettings { get; set; } = true;

    /// <summary>The current backup password (required to change it).</summary>
    [ObservableProperty]
    public partial string CurrentPassword { get; set; } = string.Empty;

    /// <summary>New backup password.</summary>
    [ObservableProperty]
    public partial string NewPassword { get; set; } = string.Empty;

    /// <summary>New backup password again.</summary>
    [ObservableProperty]
    public partial string NewPasswordRepeat { get; set; } = string.Empty;

    /// <summary>Running.</summary>
    [ObservableProperty]
    public partial bool Busy { get; set; }

    /// <summary>Whether a backup password is set (false: the page sets the first one, no current password is asked).</summary>
    [ObservableProperty]
    public partial bool HasPassword { get; set; } = true;

    /// <inheritdoc />
    public override async Task ActivateAsync()
    {
        try
        {
            var auto = await backup.AutomaticContentsAsync(CancellationToken.None).ConfigureAwait(true);
            (AutoKeys, AutoProfiles, AutoConcepts, AutoSettings) = (auto.Keys, auto.Profiles, auto.Concepts, auto.Settings);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "The contents of automatic backups could not be read");
            Status = text.Format("error.generic", ex.Message);
        }

        await RefreshAsync().ConfigureAwait(true);
    }

    /// <summary>Writes a backup now with what is ticked under "Back up now".</summary>
    [RelayCommand]
    public async Task BackupNowAsync()
    {
        var contents = new BackupContents { Keys = NowKeys, Profiles = NowProfiles, Concepts = NowConcepts, Settings = NowSettings };
        if (Problem(contents) is { } problem)
        {
            Status = problem;
            return;
        }

        Busy = true;
        try
        {
            await backup.CreateInFolderAsync(contents, CancellationToken.None).ConfigureAwait(true);
            Status = text.Format("backup.done", Describe(contents));

            // "Back up now" asks every time: the API keys tick starts unticked again.
            NowKeys = false;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Manual backup failed");
            Status = text.Format("backup.failed", ex.Message);
        }
        finally
        {
            Busy = false;
            await RefreshAsync().ConfigureAwait(true);
        }
    }

    /// <summary>Saves what automatic backups hold.</summary>
    [RelayCommand]
    public async Task SaveAutomaticAsync()
    {
        var contents = new BackupContents { Keys = AutoKeys, Profiles = AutoProfiles, Concepts = AutoConcepts, Settings = AutoSettings };
        if (Problem(contents) is { } problem)
        {
            Status = problem;
            return;
        }

        try
        {
            await backup.SaveAutomaticContentsAsync(contents, CancellationToken.None).ConfigureAwait(true);
            Status = text.Format("backup.auto.saved", Describe(contents));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "The contents of automatic backups could not be saved");
            Status = text.Format("error.generic", ex.Message);
        }
    }

    /// <summary>Changes the backup password: the current one first, then the new one twice.</summary>
    [RelayCommand]
    public async Task ChangePasswordAsync()
    {
        if (!BackupPasswordPolicy.IsAcceptable(NewPassword))
        {
            Status = text.Format("password.too_short", BackupPasswordPolicy.MinimumLength);
            return;
        }

        if (NewPassword != NewPasswordRepeat)
        {
            Status = text["password.mismatch"];
            return;
        }

        Busy = true;
        try
        {
            if (!await backup.HasPasswordAsync(CancellationToken.None).ConfigureAwait(true))
            {
                // No password yet (the first start was left before one was set): the first one is set here, there is no current one to ask.
                await backup.SetPasswordAsync(NewPassword, CancellationToken.None).ConfigureAwait(true);
                NewPassword = NewPasswordRepeat = string.Empty;
                HasPassword = true;
                Status = text["backup.password_set"];
                return;
            }

            if (!await backup.ChangePasswordAsync(CurrentPassword, NewPassword, CancellationToken.None).ConfigureAwait(true))
            {
                CurrentPassword = string.Empty;
                Status = text["backup.password.wrong"];
                return;
            }

            CurrentPassword = NewPassword = NewPasswordRepeat = string.Empty;
            Status = text["backup.password_changed"];
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Backup password change failed");
            Status = text.Format("error.generic", ex.Message);
        }
        finally
        {
            Busy = false;
        }
    }

    [RelayCommand]
    private void OpenFolder()
    {
        try
        {
            shell.OpenPath(paths.BackupDirectory);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Status = text.Format("error.generic", ex.Message);
        }
    }

    private string? Problem(BackupContents contents) =>
        !(contents.Keys || contents.Profiles || contents.Concepts || contents.Settings) ? text["backup.nothing"]
        : contents.Concepts && !contents.Profiles ? text["backup.concepts_need_profiles"]
        : null;

    private string Describe(BackupContents contents) => string.Join(", ", new[]
    {
        (contents.Keys, "backup.contents.keys"), (contents.Profiles, "backup.contents.profiles"), (contents.Concepts, "backup.contents.concepts"),
        (contents.Settings, "backup.contents.settings"),
    }.Where(p => p.Item1).Select(p => text[p.Item2]));

    private async Task RefreshAsync()
    {
        Count = backup.List().Count;
        try
        {
            HasPassword = await backup.HasPasswordAsync(CancellationToken.None).ConfigureAwait(true);
            LastBackup = await backup.LastAnyBackupAsync(CancellationToken.None).ConfigureAwait(true) is { } at
                ? at.ToLocalTime().ToString("g", text.Culture)
                : text["backup.never"];
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "The time of the last backup could not be read");
            LastBackup = text["backup.never"];
        }
    }
}
