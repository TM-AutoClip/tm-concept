using TMConcept.Core.Keys;
using TMConcept.Desktop.Dialogs;

namespace TMConcept.Tests.Desktop;

/// <summary>The key gate's dialog without a window: records the providers asked for and "adds" <see cref="Secret"/> when it is set.</summary>
internal sealed class FakeApiKeyDialog(IApiKeyStore keys) : IApiKeyDialog
{
    public List<string> Asked { get; } = [];

    public string? Secret { get; set; }

    public async Task<bool> AddKeyAsync(string provider)
    {
        Asked.Add(provider);
        if (Secret is null)
        {
            return false;
        }

        await keys.SaveAsync(new ApiKey { Provider = provider, Label = "added", Secret = Secret, LastOkAtUtc = DateTimeOffset.UtcNow }, CancellationToken.None);
        return true;
    }

    public string ProviderName(string provider) => provider;
}
