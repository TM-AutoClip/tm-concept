using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using TMConcept.Desktop.Localization;
using TMConcept.Desktop.Startup;
using TMConcept.Storage.Crypto;
using TMConcept.Storage.Database;
using TMConcept.Storage.Keys;

namespace TMConcept.Desktop.ViewModels;

/// <summary>
/// Opens a locked database: with the master password, or again with the keyring after an error. A database of another version
/// (<see cref="DatabaseVersionException"/>: older files are not upgraded) is said so plainly, and "Set aside and start fresh" renames it in
/// the data folder (never deletes it) and goes to the first start.
/// </summary>
public sealed partial class UnlockViewModel(Vault vault, UiMasterPassword masterPassword, AppSession session, Localizer text, ILogger<UnlockViewModel> logger)
    : ObservableObject
{
    /// <summary>Raised when the database is open.</summary>
    public event EventHandler? Completed;

    /// <summary>Raised when the database was set aside: the first start follows.</summary>
    public event EventHandler? StartedFresh;

    /// <summary>Whether the database is protected by a master password.</summary>
    public bool NeedsPassword => vault.Method == KeyMethods.Password;

    /// <summary>Password.</summary>
    [ObservableProperty]
    public partial string Password { get; set; } = string.Empty;

    /// <summary>Error.</summary>
    [ObservableProperty]
    public partial string Error { get; set; } = string.Empty;

    /// <summary>Running.</summary>
    [ObservableProperty]
    public partial bool Busy { get; set; }

    /// <summary>The database is of another version: only setting it aside helps (opening again cannot).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanUnlock))]
    public partial bool Incompatible { get; set; }

    /// <summary>Whether the Open button is shown.</summary>
    public bool CanUnlock => !Incompatible;

    /// <summary>Shows why a database of another version does not open.</summary>
    public void ShowIncompatible(DatabaseVersionException error)
    {
        ArgumentNullException.ThrowIfNull(error);
        Error = text[error.Newer ? "unlock.incompatible.newer" : "unlock.incompatible"];
        Incompatible = true;
    }

    /// <summary>Opens.</summary>
    [RelayCommand]
    public async Task UnlockAsync()
    {
        Busy = true;
        Error = string.Empty;
        try
        {
            if (NeedsPassword)
            {
                masterPassword.Provide(Password);
            }

            await vault.OpenAsync(CancellationToken.None).ConfigureAwait(true);
            Completed?.Invoke(this, EventArgs.Empty);
        }
        catch (SealedBoxException ex)
        {
            logger.LogWarning(ex, "Wrong master password");
            Error = text["unlock.wrong_password"];
        }
        catch (DatabaseVersionException ex)
        {
            logger.LogError(ex, "The database is of another version (schema {Found}) and is not opened", ex.Found);
            ShowIncompatible(ex);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unlock failed");
            Error = text.Format("error.generic", ex.Message);
        }
        finally
        {
            Busy = false;
        }
    }

    /// <summary>Renames the database and its key file in the data folder and goes to the first start.</summary>
    [RelayCommand]
    public void StartFresh()
    {
        try
        {
            var file = vault.SetAside();
            session.PendingMessage = text.Format("unlock.set_aside", Path.GetFileName(file));
            StartedFresh?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            logger.LogError(ex, "The database could not be set aside");
            Error = text.Format("error.generic", ex.Message);
        }
    }
}
