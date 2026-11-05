using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using TMConcept.Desktop.Localization;
using TMConcept.Desktop.Paths;
using TMConcept.Desktop.Startup;
using TMConcept.Storage.Backup;
using TMConcept.Storage.Crypto;
using TMConcept.Storage.Keys;

namespace TMConcept.Desktop.ViewModels;

/// <summary>
/// First run: language (two big buttons; Turkish first on a Turkish system), then a new start (keyring protection or a master password; the
/// backup password is required) or a restore from a backup (only now, while there is no data). Everything is in one window; a step cannot
/// finish with a required value missing.
/// </summary>
public sealed partial class FirstRunViewModel(Vault vault, BackupService backup, AppSession session, UiMasterPassword masterPassword,
    UiPreferencesFile preferences, FileDialogs dialogs, Localizer text, ILogger<FirstRunViewModel> logger) : ObservableObject
{
    /// <summary>Raised when the database is open.</summary>
    public event EventHandler? Completed;

    /// <summary>Current page.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLanguageStep), nameof(IsChoiceStep), nameof(IsNewStep), nameof(IsRestoreStep), nameof(IsFormStep))]
    public partial FirstRunStep Step { get; set; }

    /// <summary>Language page.</summary>
    public bool IsLanguageStep => Step == FirstRunStep.Language;

    /// <summary>Choice page.</summary>
    public bool IsChoiceStep => Step == FirstRunStep.Choice;

    /// <summary>New start page.</summary>
    public bool IsNewStep => Step == FirstRunStep.New;

    /// <summary>Restore page.</summary>
    public bool IsRestoreStep => Step == FirstRunStep.Restore;

    /// <summary>New or restore page.</summary>
    public bool IsFormStep => IsNewStep || IsRestoreStep;

    /// <summary>Whether the Turkish button comes first (the system language is Turkish).</summary>
    public bool TurkishFirst { get; } = Localizer.SystemLanguage() == "tr";

    /// <summary>Whether the English button comes first.</summary>
    public bool EnglishFirst => !TurkishFirst;

    /// <summary>Whether this computer has an automatic keyring (DPAPI / Secret Service).</summary>
    public bool KeyringAvailable => vault.AutomaticProtector is not null;

    /// <summary>Use a master password even though a keyring exists.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NeedsMasterPassword))]
    public partial bool UseMasterPassword { get; set; }

    /// <summary>Whether the master password fields are shown.</summary>
    public bool NeedsMasterPassword => UseMasterPassword || !KeyringAvailable;

    /// <summary>Master password.</summary>
    [ObservableProperty]
    public partial string MasterPassword { get; set; } = string.Empty;

    /// <summary>Master password again.</summary>
    [ObservableProperty]
    public partial string MasterPasswordRepeat { get; set; } = string.Empty;

    /// <summary>Backup password (required on a new start).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BackupPasswordStrength))]
    public partial string BackupPassword { get; set; } = string.Empty;

    /// <summary>Backup password again.</summary>
    [ObservableProperty]
    public partial string BackupPasswordRepeat { get; set; } = string.Empty;

    /// <summary>Strength 0-4.</summary>
    public int BackupPasswordStrength => BackupPasswordPolicy.Strength(BackupPassword);

    /// <summary>Backup file to restore.</summary>
    [ObservableProperty]
    public partial string BackupFile { get; set; } = string.Empty;

    /// <summary>Error or status.</summary>
    [ObservableProperty]
    public partial string Message { get; set; } = string.Empty;

    /// <summary>A long operation runs.</summary>
    [ObservableProperty]
    public partial bool Busy { get; set; }

    /// <summary>Turkish.</summary>
    [RelayCommand]
    private void ChooseTurkish() => ChooseLanguage("tr");

    /// <summary>English.</summary>
    [RelayCommand]
    private void ChooseEnglish() => ChooseLanguage("en");

    /// <summary>Goes to the new start page.</summary>
    [RelayCommand]
    private void ChooseNew() => Step = FirstRunStep.New;

    /// <summary>Goes to the restore page.</summary>
    [RelayCommand]
    private void ChooseRestore() => Step = FirstRunStep.Restore;

    /// <summary>Back to the choice.</summary>
    [RelayCommand]
    private void Back()
    {
        Message = string.Empty;
        Step = FirstRunStep.Choice;
    }

    /// <summary>Picks the backup file.</summary>
    [RelayCommand]
    private async Task BrowseBackupAsync()
    {
        if (await dialogs.OpenAsync("restore.pick", "backup.filter", ["*" + BackupRules.Extension]).ConfigureAwait(true) is { } file)
        {
            BackupFile = file;
        }
    }

    /// <summary>Creates the encrypted database and stores the backup key.</summary>
    [RelayCommand]
    public async Task CreateAsync()
    {
        if (!CheckMasterPassword() || !CheckBackupPassword())
        {
            return;
        }

        await RunAsync(async () =>
        {
            await vault.CreateAsync(Protector(), CancellationToken.None).ConfigureAwait(true);
            await backup.SetPasswordAsync(BackupPassword, CancellationToken.None).ConfigureAwait(true);
            await FinishAsync().ConfigureAwait(true);
        }).ConfigureAwait(true);
    }

    /// <summary>Restores a backup.</summary>
    [RelayCommand]
    public async Task RestoreAsync()
    {
        if (BackupFile.Length == 0 || BackupPassword.Length == 0)
        {
            Message = text["restore.missing"];
            return;
        }

        if (!CheckMasterPassword())
        {
            return;
        }

        await RunAsync(async () =>
        {
            var report = await backup.RestoreAsync(BackupFile, BackupPassword, Protector(), CancellationToken.None).ConfigureAwait(true);
            session.PendingMessage = text.Format("restore.done", report.Profiles, report.Concepts, report.Videos);
            await FinishAsync().ConfigureAwait(true);
        }).ConfigureAwait(true);
    }

    private void ChooseLanguage(string code)
    {
        preferences.Change(p => p.Language = code);
        text.Use(code);
        Step = FirstRunStep.Choice;
    }

    private IKeyProtector Protector()
    {
        if (NeedsMasterPassword)
        {
            masterPassword.Provide(MasterPassword);
            return vault.PasswordProtector;
        }

        return vault.AutomaticProtector!;
    }

    private bool CheckMasterPassword()
    {
        if (!NeedsMasterPassword)
        {
            return true;
        }

        if (!BackupPasswordPolicy.IsAcceptable(MasterPassword))
        {
            Message = text.Format("password.too_short", BackupPasswordPolicy.MinimumLength);
            return false;
        }

        if (MasterPassword != MasterPasswordRepeat)
        {
            Message = text["password.mismatch"];
            return false;
        }

        return true;
    }

    private bool CheckBackupPassword()
    {
        if (!BackupPasswordPolicy.IsAcceptable(BackupPassword))
        {
            Message = text.Format("password.too_short", BackupPasswordPolicy.MinimumLength);
            return false;
        }

        if (BackupPassword != BackupPasswordRepeat)
        {
            Message = text["password.mismatch"];
            return false;
        }

        return true;
    }

    private async Task FinishAsync()
    {
        await session.LoadAsync(CancellationToken.None).ConfigureAwait(true);
        var settings = session.Settings;
        settings.Language = text.Language;
        await session.SaveAsync(settings, CancellationToken.None).ConfigureAwait(true);
        Completed?.Invoke(this, EventArgs.Empty);
    }

    private async Task RunAsync(Func<Task> action)
    {
        Busy = true;
        Message = string.Empty;
        try
        {
            await action().ConfigureAwait(true);
        }
        catch (SealedBoxException ex)
        {
            logger.LogWarning(ex, "Backup could not be opened");
            Message = text["restore.wrong_password"];
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "First run step failed");
            Message = text.Format("error.generic", ex.Message);
        }
        finally
        {
            Busy = false;
        }
    }
}
