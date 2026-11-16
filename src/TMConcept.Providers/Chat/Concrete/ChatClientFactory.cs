using TMConcept.Core.Chat;
using TMConcept.Core.Endpoints;
using TMConcept.Core.Models;

namespace TMConcept.Providers.Chat;

/// <summary>
/// <see cref="IChatClientFactory"/> over every registered chat provider (keyed by code; no switch). A model of one provider can never be
/// created with another provider's client, so a key never goes to a foreign address. The models of user-added local servers
/// (<c>endpoint:&lt;id&gt;</c>) are looked up by their kind (<see cref="EndpointCodes.Kind"/>): one transport serves every server, and it
/// takes an empty key (the server's token is optional).
/// </summary>
public sealed class ChatClientFactory(IEnumerable<IChatTransport> transports) : IChatClientFactory
{
    private readonly Dictionary<string, IChatTransport> _byCode = transports.ToDictionary(t => t.Code, StringComparer.Ordinal);

    /// <inheritdoc />
    public IChatClient Create(CatalogModel model, string apiKey)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(apiKey);
        if (!_byCode.TryGetValue(EndpointCodes.Kind(model.Provider), out var transport))
        {
            throw new InvalidOperationException($"'{model.Provider}' has no chat client.");
        }

        if (transport.KeyRequired)
        {
            ArgumentException.ThrowIfNullOrEmpty(apiKey);
        }

        return transport.CreateChat(model, apiKey);
    }
}
