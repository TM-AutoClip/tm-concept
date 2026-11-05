using TMConcept.Core.Models;

namespace TMConcept.Core.Chat;

/// <summary>Creates a chat client for a model and a key (the provider's own client; the key goes only to that provider's address).</summary>
public interface IChatClientFactory
{
    /// <summary>Creates.</summary>
    IChatClient Create(CatalogModel model, string apiKey);
}
