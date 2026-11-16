using TMConcept.Core.Chat;
using TMConcept.Core.Models;

namespace TMConcept.Providers.Chat;

/// <summary>A provider that speaks chat: creates its own client for a model of its catalog.</summary>
public interface IChatTransport
{
    /// <summary>Provider code (for the transport of user-added local servers, the kind <c>endpoint</c>: their codes are <c>endpoint:&lt;id&gt;</c>).</summary>
    string Code { get; }

    /// <summary>Whether a call needs an API key (false for a local server: its token is optional).</summary>
    bool KeyRequired => true;

    /// <summary>Creates the client.</summary>
    IChatClient CreateChat(CatalogModel model, string apiKey);
}
