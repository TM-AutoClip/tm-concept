using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using TMConcept.Core.Keys;
using TMConcept.Core.Providers;
using TMConcept.Desktop.Dialogs;
using TMConcept.Desktop.Localization;
using TMConcept.Desktop.Os;

namespace TMConcept.Desktop.ViewModels;

/// <summary>
/// API keys: provider (from the registered list), label, masked key; a step-by-step guide per provider (open account → create key → paste →
/// "Test connection"); the test is a free request and shows ✓ or ✗. A provider can have several keys. Keys stay in the encrypted database and
/// never go into backups.
/// </summary>
public sealed partial class KeysSettingsViewModel(IEnumerable<IProvider> providers, IApiKeyStore keys, IConfirmDialog confirm, ShellLauncher shell,
    Localizer text, TimeProvider clock, ILogger<KeysSettingsViewModel> logger) : SettingsSectionBase
{
    /// <inheritdoc />
    public override string TitleKey => "settings.keys";

    /// <inheritdoc />
    public override int Order => 20;

    /// <summary>Providers.</summary>
    public IReadOnlyList<ProviderOption> Providers { get; } = providers.Select(p => new ProviderOption(p)).ToList();

    /// <summary>Saved keys.</summary>
    public ObservableCollection<ApiKeyRowViewModel> Keys { get; } = [];

    /// <summary>Provider of the new key.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Guide), nameof(Policy))]
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
    public string Guide => Provider is null ? text["keys.guide.pick"] : text["keys.guide." + Provider.Provider.Code];

    /// <summary>Data use of the chosen provider (one line).</summary>
    public string Policy => Provider is null ? string.Empty : text["keys.policy." + Provider.Provider.Code];

    /// <inheritdoc />
    public override async Task ActivateAsync()
    {
        Provider ??= Providers.FirstOrDefault();
        await ReloadAsync().ConfigureAwait(true);
    }

    /// <summary>Saves the new key and tests it.</summary>
    [RelayCommand]
    public async Task AddAsync()
    {
        if (Provider is null || Secret.Trim().Length < 8)
        {
            Status = text["keys.invalid"];
            return;
        }

        var key = new ApiKey
        {
            Provider = Provider.Provider.Code,
            Label = Label.Trim().Length > 0 ? Label.Trim() : text["keys.default_label"],
            Secret = Secret.Trim(),
        };
        await keys.SaveAsync(key, CancellationToken.None).ConfigureAwait(true);
        Secret = string.Empty;
        Label = string.Empty;
        await ReloadAsync().ConfigureAwait(true);
        if (Keys.FirstOrDefault(k => k.Key.Id == key.Id) is { } row)
        {
            await TestAsync(row).ConfigureAwait(true);
        }
    }

    /// <summary>Tests a saved key with a free request.</summary>
    [RelayCommand]
    public async Task TestAsync(ApiKeyRowViewModel? row)
    {
        if (row is null || Providers.FirstOrDefault(p => p.Provider.Code == row.Key.Provider) is not { } provider)
        {
            return;
        }

        Busy = true;
        try
        {
            var result = await provider.Provider.TestAsync(row.Key.Secret, CancellationToken.None).ConfigureAwait(true);
            row.Failed = !result.Ok;
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
            Keys.Remove(row);
        }
    }

    /// <summary>Opens the provider's key page in the browser.</summary>
    [RelayCommand]
    private void OpenKeyPage()
    {
        if (Provider is null)
        {
            return;
        }

        try
        {
            shell.Open(Provider.Provider.KeyPage);
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
            var name = Providers.FirstOrDefault(p => p.Provider.Code == key.Provider)?.Name ?? key.Provider;
            Keys.Add(new ApiKeyRowViewModel(key, name)
            {
                Result = key.LastOkAtUtc is { } ok ? "✓ " + ok.ToLocalTime().ToString("g", text.Culture) : string.Empty,
            });
        }
    }
}
