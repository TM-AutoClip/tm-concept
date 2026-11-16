using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using TMConcept.Desktop.Dialogs;
using TMConcept.Desktop.Localization;
using TMConcept.Desktop.Startup;
using TMConcept.Storage.Crypto;
using TMConcept.Storage.Database;
using TMConcept.Storage.Keys;

namespace TMConcept.Desktop.ViewModels;

/// <summary>
/// Opens a locked database: with the master password, or again with the keyring after an error. Opening brings an older database to this
/// version's schema (a copy of it is kept first). A database this version cannot open says why (<see cref="DatabaseVersionException"/>);
/// one made before upgrades were recorded (0.1.16 or earlier) can be set aside after a confirmation (renamed with its key file, never
/// deleted), and the first start follows (new start or restore).
/// </summary>
public sealed partial class UnlockViewModel(Vault vault, UiMasterPassword masterPassword, IConfirmDialog confirm, Localizer text, Updates.SelfReplacer replacer,
    ILogger<UnlockViewModel> logger)
    : ObservableObject
{
    /// <summary>Raised when the database is open.</summary>
    public event EventHandler? Completed;

    /// <summary>Raised when the database was set aside: the first start comes next.</summary>
    public event EventHandler? StartFresh;

    /// <summary>Whether the database is protected by a master password.</summary>
    public bool NeedsPassword => vault.Method == KeyMethods.Password;

    /// <summary>Password.</summary>
    [ObservableProperty]
    public partial string Password { get; set; } = string.Empty;

    /// <summary>Error.</summary>
    [ObservableProperty]
    public partial string Error { get; set; } = string.Empty;

    /// <summary>Whether "Set it aside and start fresh" is offered (a database made before upgrades were recorded).</summary>
    [ObservableProperty]
    public partial bool CanSetAside { get; set; }

    /// <summary>Running.</summary>
    [ObservableProperty]
    public partial bool Busy { get; set; }

    /// <summary>The text for a database that could not be opened, and whether it can be set aside.</summary>
    public static (string Text, bool CanSetAside) Describe(DatabaseVersionException error, Localizer text)
    {
        ArgumentNullException.ThrowIfNull(error);
        ArgumentNullException.ThrowIfNull(text);
        return error.Problem switch
        {
            DatabaseVersionProblem.Unrecorded => (text["database.unrecorded"], true),
            DatabaseVersionProblem.Newer => (text.Format("database.newer", error.Step), false),
            _ => (text.Format("database.failed", error.Step, error.InnerException?.Message ?? error.Message), false),
        };
    }

    /// <summary>Shows why the database was not opened.</summary>
    public void Show(DatabaseVersionException error)
    {
        (Error, CanSetAside) = Describe(error, text, replacer.PreviousProgram);
    }

    /// <summary>
    /// The text for a database that could not be opened, and whether it can be set aside; <paramref name="previousProgram"/> (the program
    /// file an update just replaced, still kept) adds how to go back to it when this version cannot use the database.
    /// </summary>
    public static (string Text, bool CanSetAside) Describe(DatabaseVersionException error, Localizer text, string? previousProgram)
    {
        var (message, canSetAside) = Describe(error, text);
        return previousProgram is { Length: > 0 } && error.Problem != DatabaseVersionProblem.Unrecorded
            ? (message + "\n" + text.Format("database.previous_program", Path.GetFileName(previousProgram)), canSetAside)
            : (message, canSetAside);
    }

    /// <summary>Opens.</summary>
    [RelayCommand]
    public async Task UnlockAsync()
    {
        Busy = true;
        Error = string.Empty;
        CanSetAside = false;
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
            logger.LogError(ex, "The database was not opened: {Problem}", ex.Problem);
            Show(ex);
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

    /// <summary>Sets the database aside (after a confirmation) so the first start can make a new one or restore a backup.</summary>
    [RelayCommand]
    public async Task SetAsideAsync()
    {
        var answer = await confirm.AskAsync(text["unlock.set_aside.title"], text["unlock.set_aside.message"], text["unlock.set_aside"], danger: true)
            .ConfigureAwait(true);
        if (!answer.Confirmed)
        {
            return;
        }

        try
        {
            vault.SetAside();
            StartFresh?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogError(ex, "The database could not be set aside");
            Error = text.Format("error.generic", ex.Message);
        }
    }
}
