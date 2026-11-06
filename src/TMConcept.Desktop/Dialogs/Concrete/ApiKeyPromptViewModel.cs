using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using TMConcept.Core.Keys;
using TMConcept.Core.Providers;
using TMConcept.Desktop.Localization;
using TMConcept.Desktop.Os;

namespace TMConcept.Desktop.Dialogs;

/// <summary>
/// Content of the key gate's window: which provider needs a key and why, its step-by-step guide, the key's label and value, and "Test and
/// save". The key is tested first with the provider's free request and saved only when it works (fal has no free test: its key is saved
/// with a note and checked at its first use). The key is never logged or shown again.
/// </summary>
public sealed partial class ApiKeyPromptViewModel(IProvider provider, IApiKeyStore keys, ShellLauncher shell, Localizer text, TimeProvider clock,
    ILogger logger) : ObservableObject
{
    /// <summary>Raised when the window should close (a key was saved).</summary>
    public event EventHandler? CloseRequested;

    /// <summary>Title: "An API key of … is needed".</summary>
    public string Title => text.Format("keygate.title", provider.Name);

    /// <summary>Why it is asked.</summary>
    public string Message => text.Format("keygate.message", provider.Name);

    /// <summary>The provider's guide.</summary>
    public string Guide => text["keys.guide." + provider.Code];

    /// <summary>The provider's data use (one line).</summary>
    public string Policy => text["keys.policy." + provider.Code];

    /// <summary>Cancel button text.</summary>
    public string CancelText => text["common.cancel"];

    /// <summary>Label of the key.</summary>
    [ObservableProperty]
    public partial string Label { get; set; } = string.Empty;

    /// <summary>The key.</summary>
    [ObservableProperty]
    public partial string Secret { get; set; } = string.Empty;

    /// <summary>Testing.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Idle))]
    public partial bool Busy { get; set; }

    /// <summary>Not testing.</summary>
    public bool Idle => !Busy;

    /// <summary>The outcome of the last test.</summary>
    [ObservableProperty]
    public partial string Result { get; set; } = string.Empty;

    /// <summary>Whether the last test failed.</summary>
    [ObservableProperty]
    public partial bool Failed { get; set; }

    /// <summary>Whether a working key was saved.</summary>
    public bool Saved { get; private set; }

    /// <summary>Tests the key; saves it only when the test passes, then closes.</summary>
    [RelayCommand]
    public async Task TestAndSaveAsync()
    {
        var secret = Secret.Trim();
        if (Busy)
        {
            return;
        }

        if (secret.Length < 8)
        {
            Failed = true;
            Result = text["keys.invalid"];
            return;
        }

        Busy = true;
        try
        {
            var result = await provider.TestAsync(secret, CancellationToken.None).ConfigureAwait(true);
            logger.LogInformation("Key test of {Provider} in the key gate: {Code}", provider.Code, result.Code);
            if (!result.Ok)
            {
                Failed = true;
                Result = "✗ " + text.Format("keys.test.failed", ProviderErrors.Text(text, result.Code)) + " " + text["keygate.not_saved"];
                logger.LogWarning("The key of {Provider} did not pass its test and was not saved ({Code})", provider.Code, result.Code);
                return;
            }

            var key = new ApiKey
            {
                Provider = provider.Code,
                Label = Label.Trim().Length > 0 ? Label.Trim() : text["keys.default_label"],
                Secret = secret,
                LastOkAtUtc = result.Code == "ok" ? clock.GetUtcNow() : null,
            };
            await keys.SaveAsync(key, CancellationToken.None).ConfigureAwait(true);
            logger.LogInformation("An API key was saved for {Provider} from the key gate", provider.Code);
            Secret = string.Empty;
            Failed = false;
            Saved = true;
            Result = result.Code == "ok" ? "✓ " + text["keys.test.ok"] : "• " + text["keys.test.untested"];
            CloseRequested?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The message never holds the key: it comes from the network or storage layer, which does not see the value.
            logger.LogError(ex, "The key of {Provider} could not be tested or saved", provider.Code);
            Failed = true;
            Result = "✗ " + text.Format("error.generic", ex.Message);
        }
        finally
        {
            Busy = false;
        }
    }

    /// <summary>Opens the provider's key page in the browser.</summary>
    [RelayCommand]
    private void OpenKeyPage()
    {
        try
        {
            shell.Open(provider.KeyPage);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            logger.LogWarning(ex, "The key page of {Provider} could not be opened", provider.Code);
            Result = text.Format("error.generic", ex.Message);
        }
    }
}
