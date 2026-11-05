using TMConcept.Core.Chat;
using TMConcept.Core.Models;

namespace TMConcept.Providers.Chat;

/// <summary>
/// <see cref="IChatClientFactory"/> over every registered chat provider (keyed by code; no switch). A model of one provider can never be
/// created with another provider's client, so a key never goes to a foreign address.
/// </summary>
public sealed class ChatClientFactory(IEnumerable<IChatTransport> transports) : IChatClientFactory
{
    private readonly Dictionary<string, IChatTransport> _byCode = transports.ToDictionary(t => t.Code, StringComparer.Ordinal);

    /// <inheritdoc />
    public IChatClient Create(CatalogModel model, string apiKey)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentException.ThrowIfNullOrEmpty(apiKey);
        return _byCode.TryGetValue(model.Provider, out var transport)
            ? transport.CreateChat(model, apiKey)
            : throw new InvalidOperationException($"'{model.Provider}' has no chat client.");
    }
}
