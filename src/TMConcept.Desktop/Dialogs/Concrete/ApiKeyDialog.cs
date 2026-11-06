using Microsoft.Extensions.Logging;
using TMConcept.Core.Keys;
using TMConcept.Core.Providers;
using TMConcept.Desktop.Localization;
using TMConcept.Desktop.Os;
using TMConcept.Desktop.Startup;
using TMConcept.Desktop.Views;

namespace TMConcept.Desktop.Dialogs;

/// <summary><see cref="IApiKeyDialog"/> as a modal window over the active window (no window = no key, logged).</summary>
public sealed class ApiKeyDialog(WindowHost host, IEnumerable<IProvider> providers, IApiKeyStore keys, ShellLauncher shell, Localizer text, TimeProvider clock,
    ILogger<ApiKeyDialog> logger) : IApiKeyDialog
{
    private readonly IReadOnlyList<IProvider> _providers = providers.ToList();

    /// <inheritdoc />
    public async Task<bool> AddKeyAsync(string provider)
    {
        if (Prompt(provider) is not { } model)
        {
            return false;
        }

        if (host.Active is not { } owner)
        {
            logger.LogWarning("No window is open to ask for a key of {Provider}", provider);
            return false;
        }

        var window = new ApiKeyWindow { DataContext = model };
        model.CloseRequested += (_, _) => window.Close();
        await window.ShowDialog(owner).ConfigureAwait(true);
        return model.Saved;
    }

    /// <summary>The content of the window for a provider (null and logged for a provider the app does not know).</summary>
    public ApiKeyPromptViewModel? Prompt(string provider)
    {
        if (_providers.FirstOrDefault(p => p.Code == provider) is not { } known)
        {
            logger.LogError("A key was asked for the unknown provider {Provider}", provider);
            return null;
        }

        return new ApiKeyPromptViewModel(known, keys, shell, text, clock, logger);
    }

    /// <inheritdoc />
    public string ProviderName(string provider) => _providers.FirstOrDefault(p => p.Code == provider)?.Name ?? provider;
}
