using TMConcept.Core.Chat;
using TMConcept.Core.Models;

namespace TMConcept.Providers.Chat;

/// <summary>A provider that speaks chat: creates its own client for a model of its catalog.</summary>
public interface IChatTransport
{
    /// <summary>Provider code.</summary>
    string Code { get; }

    /// <summary>Creates the client.</summary>
    IChatClient CreateChat(CatalogModel model, string apiKey);
}
