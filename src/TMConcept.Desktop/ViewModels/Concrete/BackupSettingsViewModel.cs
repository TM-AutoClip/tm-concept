using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using TMConcept.Core.Paths;
using TMConcept.Desktop.Localization;
using TMConcept.Desktop.Os;
using TMConcept.Desktop.Startup;
using TMConcept.Storage.Backup;

namespace TMConcept.Desktop.ViewModels;

/// <summary>
/// Backup settings: back up now, change the backup password, open the backup folder, last backup. Backups are daily and the last ten are kept;
/// API keys, videos and sample images are not in them.
/// </summary>
public sealed partial class BackupSettingsViewModel(BackupService backup, AppSession session, IAppPaths paths, ShellLauncher shell, Localizer text,
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

    /// <summary>New backup password.</summary>
    [ObservableProperty]
    public partial string NewPassword { get; set; } = string.Empty;

    /// <summary>New backup password again.</summary>
    [ObservableProperty]
    public partial string NewPasswordRepeat { get; set; } = string.Empty;

    /// <summary>Running.</summary>
    [ObservableProperty]
    public partial bool Busy { get; set; }

    /// <inheritdoc />
    public override Task ActivateAsync()
    {
        Refresh();
        return Task.CompletedTask;
    }

    [RelayCommand]
    private async Task BackupNowAsync()
    {
        Busy = true;
        try
        {
            await backup.CreateInFolderAsync(CancellationToken.None).ConfigureAwait(true);
            Status = text["backup.done"];
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Manual backup failed");
            Status = text.Format("backup.failed", ex.Message);
        }
        finally
        {
            Busy = false;
            Refresh();
        }
    }

    [RelayCommand]
    private async Task ChangePasswordAsync()
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
            await backup.SetPasswordAsync(NewPassword, CancellationToken.None).ConfigureAwait(true);
            NewPassword = NewPasswordRepeat = string.Empty;
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

    private void Refresh()
    {
        Count = backup.List().Count;
        LastBackup = session.Settings.LastBackupUtc is { } at ? at.ToLocalTime().ToString("g", text.Culture) : text["backup.never"];
    }
}
