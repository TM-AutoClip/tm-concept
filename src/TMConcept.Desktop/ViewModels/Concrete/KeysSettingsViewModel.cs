using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using TMConcept.Core.Keys;
using TMConcept.Core.Models;
using TMConcept.Core.Providers;
using TMConcept.Desktop.Dialogs;
using TMConcept.Desktop.Localization;
using TMConcept.Desktop.Os;

namespace TMConcept.Desktop.ViewModels;

/// <summary>
/// API keys: provider (from the registered list), label, masked key; a step-by-step guide per provider (open account → create key → paste →
/// "Test connection"); the test is a free request and shows ✓ or ✗; a new key is saved only when it passes (or when the provider has no free
/// test). A provider can have several keys. Keys stay in the encrypted database and go into a password-encrypted backup only when ticked.
/// The last entry of the provider list adds a local server (OpenAI-compatible) instead of a key (<see cref="Endpoint"/>).
/// </summary>
public sealed partial class KeysSettingsViewModel(IEnumerable<IProvider> providers, IApiKeyStore keys, IConfirmDialog confirm, ShellLauncher shell,
    Localizer text, TimeProvider clock, EndpointEditorViewModel endpoint, ILogger<KeysSettingsViewModel> logger) : SettingsSectionBase
{
    /// <inheritdoc />
    public override string TitleKey => "settings.keys";

    /// <inheritdoc />
    public override int Order => 20;

    /// <summary>Providers.</summary>
    public IReadOnlyList<ProviderOption> Providers { get; } =
        [.. providers.Select(p => new ProviderOption(p)), new ProviderOption(ModelProviders.Endpoint, text["keys.endpoint.provider"], null)];

    /// <summary>Adding a local server (shown when its entry is chosen).</summary>
    public EndpointEditorViewModel Endpoint { get; } = endpoint;

    /// <summary>The local server entry is chosen: its form replaces the key fields.</summary>
    public bool IsEndpoint => Provider?.IsEndpoint ?? false;

    /// <summary>An online provider is chosen: the key fields and its key page are shown.</summary>
    public bool IsKey => !IsEndpoint;

    /// <summary>Saved keys.</summary>
    public ObservableCollection<ApiKeyRowViewModel> Keys { get; } = [];

    /// <summary>Provider of the new key.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Guide), nameof(Policy), nameof(IsEndpoint), nameof(IsKey))]
    public partial ProviderOption? Provider { get; set; }

    /// <summary>Label of the new key.</summary>
    [ObservableProperty]
    public partial string Label { get; set; } = string.Empty;

    /// <summary>The new key.</summary>
    [ObservableProperty]
    public partial string Secret { get; set; } = string.Empty;

    /// <summary>Running.</summary>
    [ObservableProperty]
    public partial bool Busy { get; set; }

    /// <summary>Guide of the chosen provider.</summary>
    public string Guide => Provider is null ? text["keys.guide.pick"] : text["keys.guide." + Provider.Code];

    /// <summary>Data use of the chosen provider (one line).</summary>
    public string Policy => Provider is null ? string.Empty : text["keys.policy." + Provider.Code];

    /// <inheritdoc />
    public override async Task ActivateAsync()
    {
        Provider ??= Providers.FirstOrDefault();
        Endpoint.Reload();
        await ReloadAsync().ConfigureAwait(true);
    }

    /// <summary>Tests the new key with the provider's free request and saves it only when it works (or when no free test exists: fal).</summary>
    [RelayCommand]
    public async Task AddAsync()
    {
        if (Provider is { IsEndpoint: true })
        {
            await Endpoint.SaveAsync().ConfigureAwait(true);
            return;
        }

        if (Provider?.Provider is not { } provider || Secret.Trim().Length < 8)
        {
            Status = text["keys.invalid"];
            return;
        }

        if (Busy)
        {
            return;
        }

        var key = new ApiKey
        {
            Provider = provider.Code,
            Label = Label.Trim().Length > 0 ? Label.Trim() : text["keys.default_label"],
            Secret = Secret.Trim(),
        };
        Busy = true;
        ConnectionTest result;
        try
        {
            result = await provider.TestAsync(key.Secret, CancellationToken.None).ConfigureAwait(true);
            logger.LogInformation("Key test of {Provider} before saving: {Code}", provider.Code, result.Code);
            if (!result.Ok)
            {
                logger.LogWarning("The key of {Provider} did not pass its test and was not saved ({Code})", provider.Code, result.Code);
                Status = "✗ " + text.Format("keys.test.failed", ProviderErrors.Text(text, result.Code)) + " " + text["keygate.not_saved"];
                return;
            }

            key.LastOkAtUtc = result.Code == "ok" ? clock.GetUtcNow() : null;
            await keys.SaveAsync(key, CancellationToken.None).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The message never holds the key: it comes from the network or storage layer, which does not see the value.
            logger.LogError(ex, "An API key could not be tested or saved ({Provider})", key.Provider);
            Status = text.Format("error.generic", ex.Message);
            return;
        }
        finally
        {
            Busy = false;
        }

        logger.LogInformation("An API key was saved for {Provider}", key.Provider);
        Secret = string.Empty;
        Label = string.Empty;
        Status = result.Code == "ok" ? "✓ " + text["keys.test.ok"] : "• " + text["keys.test.untested"];
        await ReloadAsync().ConfigureAwait(true);
        if (Keys.FirstOrDefault(k => k.Key.Id == key.Id) is { } row)
        {
            row.Result = Status;
        }
    }

    /// <summary>Tests a saved key with a free request.</summary>
    [RelayCommand]
    public async Task TestAsync(ApiKeyRowViewModel? row)
    {
        if (row is null || Providers.FirstOrDefault(p => p.Code == row.Key.Provider)?.Provider is not { } provider)
        {
            return;
        }

        Busy = true;
        try
        {
            var result = await provider.TestAsync(row.Key.Secret, CancellationToken.None).ConfigureAwait(true);
            row.Failed = !result.Ok;
            logger.LogInformation("Key test of {Provider}: {Code}", row.Key.Provider, result.Code);
            row.Result = result.Code switch
            {
                "ok" => "✓ " + text["keys.test.ok"],
                "untested" => "• " + text["keys.test.untested"],
                _ => "✗ " + text.Format("keys.test.failed", ProviderErrors.Text(text, result.Code)),
            };
            if (result.Code == "ok")
            {
                await keys.MarkOkAsync(row.Key.Id, clock.GetUtcNow(), CancellationToken.None).ConfigureAwait(true);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Key test failed");
            row.Failed = true;
            row.Result = "✗ " + text.Format("error.generic", ex.Message);
        }
        finally
        {
            Busy = false;
        }
    }

    /// <summary>Deletes a key after a confirmation.</summary>
    [RelayCommand]
    public async Task DeleteAsync(ApiKeyRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        var answer = await confirm.AskAsync(text["keys.delete.title"], text.Format("keys.delete.message", row.ProviderName, row.Label),
            text["common.delete"], danger: true).ConfigureAwait(true);
        if (answer.Confirmed)
        {
            await keys.DeleteAsync(row.Key.Id, CancellationToken.None).ConfigureAwait(true);
            logger.LogInformation("An API key of {Provider} was deleted", row.Key.Provider);
            Keys.Remove(row);
        }
    }

    /// <summary>Opens the provider's key page in the browser.</summary>
    [RelayCommand]
    private void OpenKeyPage()
    {
        if (Provider?.Provider is not { } provider)
        {
            return;
        }

        try
        {
            shell.Open(provider.KeyPage);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Status = text.Format("error.generic", ex.Message);
        }
    }

    private async Task ReloadAsync()
    {
        Keys.Clear();
        foreach (var key in await keys.ListAsync(null, CancellationToken.None).ConfigureAwait(true))
        {
            var name = Providers.FirstOrDefault(p => p.Code == key.Provider)?.Name ?? key.Provider;
            Keys.Add(new ApiKeyRowViewModel(key, name)
            {
                Result = key.LastOkAtUtc is { } ok ? "✓ " + ok.ToLocalTime().ToString("g", text.Culture) : string.Empty,
            });
        }
    }
}
